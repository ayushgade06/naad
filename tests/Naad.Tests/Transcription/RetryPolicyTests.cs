using System.Net;
using System.Text;
using Naad.Transcription;

namespace Naad.Tests.Transcription;

// The policy itself, then the transcriber following it against a fake HttpMessageHandler. The
// transcriber logs a retry, so these share the Log collection.
[Collection("Log")]
public sealed class RetryPolicyTests
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";
    private static readonly Uri BaseUrl = new("https://stt.example.test/openai/v1");
    private static readonly byte[] Wav = [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4];

    [Theory]
    // The request never reached the server: safe to send again, once.
    [InlineData(HttpRequestError.NameResolutionError, 1, true)]
    [InlineData(HttpRequestError.ConnectionError, 1, true)]
    [InlineData(HttpRequestError.SecureConnectionError, 1, true)]
    [InlineData(HttpRequestError.ConnectionError, 2, false)]
    // The server may have received it: never sent twice.
    [InlineData(HttpRequestError.ResponseEnded, 1, false)]
    [InlineData(HttpRequestError.InvalidResponse, 1, false)]
    [InlineData(HttpRequestError.Unknown, 1, false)]
    public void ShouldRetry_Error_RetriesOnlyUnsentRequestsOnce(HttpRequestError error, int attempts, bool expected)
    {
        Assert.Equal(expected, RetryPolicy.ShouldRetry(error, attempts));
    }

    [Fact]
    public async Task Transcribe_ConnectionFailsOnce_RetriesAndSucceeds()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
            _ => Ok());

        var result = await Transcribe(handler);

        Assert.Equal(new TranscriptionResult("hello"), result);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Transcribe_ConnectionFailsTwice_FailsAfterOneRetry()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no host"),
            _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no host"),
            _ => Ok());

        var result = await Transcribe(handler);

        Assert.Equal(
            new TranscriptionResult("", new(TranscriptionError.Network, (int)HttpRequestError.NameResolutionError)),
            result);
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpRequestError.ResponseEnded)]
    [InlineData(HttpRequestError.InvalidResponse)]
    public async Task Transcribe_ResponseLost_NeverSendsTwice(HttpRequestError error)
    {
        var handler = new SequenceHandler(_ => throw new HttpRequestException(error, "dropped"), _ => Ok());

        var result = await Transcribe(handler);

        Assert.Equal(TranscriptionError.Network, result.Failure?.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transcribe_ServerError_IsNotRetried()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), _ => Ok());

        var result = await Transcribe(handler);

        Assert.Equal(TranscriptionError.ServerError, result.Failure?.Error);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transcribe_BudgetEndsDuringRetryDelay_ReturnsTimeoutWithoutResending()
    {
        var handler = new SequenceHandler(
            _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"),
            _ => Ok());

        var result = await Transcribe(handler, timeout: TimeSpan.FromMilliseconds(200));

        Assert.Equal(new TranscriptionResult("", new(TranscriptionError.Timeout)), result);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Transcribe_CallerCancelsDuringRetryDelay_Throws()
    {
        using var cancel = new CancellationTokenSource();
        var handler = new SequenceHandler(
            _ =>
            {
                cancel.Cancel();
                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            },
            _ => Ok());
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, "m");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcriber.TranscribeAsync(Wav, cancel.Token));
        Assert.Equal(1, handler.Calls);
    }

    private static async Task<TranscriptionResult> Transcribe(SequenceHandler handler, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, "m", timeout: timeout);
        return await transcriber.TranscribeAsync(Wav, CancellationToken.None);
    }

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "text": "hello" }""", Encoding.UTF8, "application/json"),
        };

    // Answers the n-th request with the n-th function.
    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return Task.FromResult(responses[Calls++](request));
            }
            catch (Exception e)
            {
                return Task.FromException<HttpResponseMessage>(e);
            }
        }
    }
}
