using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Naad.Transcription;

namespace Naad.Tests.Transcription;

// Every test runs against a fake HttpMessageHandler; nothing here touches the network. The
// filler tests write to the shared Log, so they must not run alongside tests that read its file.
[Collection("Log")]
public sealed class OpenAiCompatibleTranscriberTests
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";
    private static readonly Uri BaseUrl = new("https://stt.example.test/openai/v1");
    private static readonly byte[] Wav = [0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4];

    [Fact]
    public async Task Transcribe_Ok_ReturnsTrimmedText()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "text": " hello world" }"""));

        var result = await Transcribe(handler);

        Assert.Equal(new TranscriptionResult("hello world"), result);
    }

    [Fact]
    public async Task Transcribe_Ok_PostsMultipartWithModelAndAudioToEndpoint()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "text": "hi" }"""));

        await Transcribe(handler, model: "custom-model-1");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://stt.example.test/openai/v1/audio/transcriptions", handler.Uri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", FakeKey), handler.Authorization);
        Assert.Equal("multipart/form-data", handler.ContentType);
        Assert.Collection(
            handler.Parts,
            part => Assert.Equal(new Part("model", null, "text/plain", "custom-model-1"u8.ToArray()), part),
            part => Assert.Equal(new Part("response_format", null, "text/plain", "json"u8.ToArray()), part),
            part => Assert.Equal(new Part("file", "audio.wav", "audio/wav", Wav), part));
    }

    [Fact]
    public async Task Transcribe_WithLanguage_SendsItBeforeTheAudio()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "text": "hi" }"""));

        await Transcribe(handler, language: "hi");

        Assert.Equal(["model", "response_format", "language", "file"], handler.Parts.Select(p => p.Name));
        Assert.Equal("hi"u8.ToArray(), handler.Parts[2].Bytes);
    }

    [Theory]
    [InlineData("https://stt.example.test/openai/v1")]
    [InlineData("https://stt.example.test/openai/v1/")]
    public void Endpoint_WithOrWithoutTrailingSlash_AppendsPathOnce(string baseUrl)
    {
        using var http = new HttpClient();

        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, new Uri(baseUrl), "m");

        Assert.Equal("https://stt.example.test/openai/v1/audio/transcriptions", transcriber.Endpoint.AbsoluteUri);
    }

    [Theory]
    [InlineData(400, "Rejected")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Unauthorized")]
    [InlineData(404, "Rejected")]
    [InlineData(413, "TooLarge")]
    [InlineData(422, "Rejected")]
    [InlineData(429, "RateLimited")]
    [InlineData(500, "ServerError")]
    [InlineData(502, "ServerError")]
    [InlineData(503, "ServerError")]
    public async Task Transcribe_ErrorStatus_ReturnsTypedFailureWithStatus(int status, string expected)
    {
        // The body echoes text that must never come back as a transcript.
        using var handler = new FakeHandler(_ => Json((HttpStatusCode)status, """{ "text": "echoed" }"""));

        var result = await Transcribe(handler);

        Assert.Equal("", result.Text);
        Assert.Equal(expected, result.Failure?.Error.ToString());
        Assert.Equal(status, result.Failure?.Code);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task Transcribe_AnyResponse_DisposesIt(int status)
    {
        var content = new TrackedContent("""{ "text": "hi" }""");
        using var handler = new FakeHandler(_ => new HttpResponseMessage((HttpStatusCode)status) { Content = content });

        await Transcribe(handler);

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task Transcribe_ConnectionFails_ReturnsNetworkWithRequestError()
    {
        using var handler = FakeHandler.Throwing(
            new HttpRequestException(HttpRequestError.NameResolutionError, "no such host"));

        var result = await Transcribe(handler);

        Assert.Equal(
            new TranscriptionResult("", new(TranscriptionError.Network, (int)HttpRequestError.NameResolutionError)),
            result);
    }

    [Fact]
    public async Task Transcribe_ConnectionDropsMidResponse_ReturnsNetwork()
    {
        using var handler = FakeHandler.Throwing(new IOException("connection reset"));

        var result = await Transcribe(handler);

        Assert.Equal(TranscriptionError.Network, result.Failure?.Error);
    }

    [Fact]
    public async Task Transcribe_NoResponseBeforeTimeout_ReturnsTimeout()
    {
        using var handler = new FakeHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

        var result = await Transcribe(handler, timeout: TimeSpan.FromMilliseconds(50));

        Assert.Equal(new TranscriptionResult("", new(TranscriptionError.Timeout)), result);
    }

    [Fact]
    public async Task Transcribe_CallerCancels_ThrowsInsteadOfFailing()
    {
        var started = new TaskCompletionSource();
        using var handler = new FakeHandler(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, "m", timeout: TimeSpan.FromHours(1));
        using var cancel = new CancellationTokenSource();

        var transcribing = transcriber.TranscribeAsync(Wav, cancel.Token);
        await started.Task;
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transcribing);
    }

    [Fact]
    public async Task Transcribe_AlreadyCancelled_ThrowsWithoutSending()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, """{ "text": "hi" }"""));
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, "m");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transcriber.TranscribeAsync(Wav, new CancellationToken(canceled: true)));
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("""{ "text": "" }""")]
    [InlineData("""{ "text": "   " }""")]
    [InlineData("""{ "text": " Thanks for watching!" }""")]
    public async Task Transcribe_EmptyOrFillerText_ReturnsEmptySuccess(string body)
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, body));

        var result = await Transcribe(handler);

        Assert.Equal(new TranscriptionResult(""), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{ "transcript": "hi" }""")]
    [InlineData("""{ "text": 5 }""")]
    [InlineData("""{ "text": null }""")]
    [InlineData("""{ "text": "cut off""")]
    public async Task Transcribe_UnexpectedBody_ReturnsMalformedResponse(string body)
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, body));

        var result = await Transcribe(handler);

        Assert.Equal(new TranscriptionResult("", new(TranscriptionError.MalformedResponse)), result);
    }

    [Theory]
    [InlineData("", "m", "https://x.test/v1")]
    [InlineData(" ", "m", "https://x.test/v1")]
    [InlineData(FakeKey, "", "https://x.test/v1")]
    [InlineData("ï»¿" + FakeKey, "m", "https://x.test/v1")]
    [InlineData("fake key", "m", "https://x.test/v1")]
    [InlineData(FakeKey, "m", "ftp://x.test/v1")]
    [InlineData(FakeKey, "m", "file:///C:/v1")]
    public void Constructor_InvalidConfiguration_Throws(string key, string model, string baseUrl)
    {
        using var http = new HttpClient();

        var error = Assert.ThrowsAny<ArgumentException>(
            () => new OpenAiCompatibleTranscriber(http, key, new Uri(baseUrl), model));
        Assert.DoesNotContain(FakeKey, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("english")]
    [InlineData("")]
    public void Constructor_InvalidLanguage_Throws(string language)
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(() => new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, "m", language));
    }

    [Theory]
    [InlineData(200, "Accepted", null)]
    [InlineData(204, "Accepted", null)]
    [InlineData(404, "CannotVerify", null)]
    [InlineData(405, "CannotVerify", null)]
    [InlineData(401, "Failed", "Unauthorized")]
    [InlineData(403, "Failed", "Unauthorized")]
    [InlineData(429, "Failed", "RateLimited")]
    [InlineData(500, "Failed", "ServerError")]
    public async Task TestKey_Status_MapsToOutcome(int status, string outcome, string? error)
    {
        using var handler = new FakeHandler(_ => Json((HttpStatusCode)status, """{ "data": [] }"""));

        var result = await TestKey(handler);

        Assert.Equal(outcome, result.Outcome.ToString());
        Assert.Equal(error, result.Failure?.Error.ToString());
    }

    [Fact]
    public async Task TestKey_SendsGetToModelsWithTheKeyAndNoBody()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));

        await TestKey(handler);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://stt.example.test/openai/v1/models", handler.Uri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", FakeKey), handler.Authorization);
        Assert.Null(handler.ContentType);
    }

    [Fact]
    public async Task TestKey_AnyResponse_DisposesItUnread()
    {
        var content = new TrackedContent("""{ "data": [] }""");
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });

        await TestKey(handler);

        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task TestKey_NoResponseBeforeTimeout_FailsWithTimeout()
    {
        using var handler = new FakeHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

        var result = await TestKey(handler, TimeSpan.FromMilliseconds(50));

        Assert.Equal(new KeyTestResult(KeyTestOutcome.Failed, new(TranscriptionError.Timeout)), result);
    }

    [Fact]
    public async Task TestKey_ConnectionFails_FailsWithNetwork()
    {
        using var handler = FakeHandler.Throwing(
            new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var result = await TestKey(handler);

        Assert.Equal(
            new KeyTestResult(
                KeyTestOutcome.Failed, new(TranscriptionError.Network, (int)HttpRequestError.ConnectionError)),
            result);
    }

    [Fact]
    public async Task TestKey_AlreadyCancelled_ThrowsWithoutSending()
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenAiCompatibleTranscriber.TestKeyAsync(
            http, FakeKey, BaseUrl, new CancellationToken(canceled: true)));
        Assert.Null(handler.Method);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fake key")]
    public async Task TestKey_InvalidKey_ThrowsWithoutSendingOrEchoingIt(string key)
    {
        using var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "{}"));
        using var http = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => OpenAiCompatibleTranscriber.TestKeyAsync(http, key, BaseUrl, CancellationToken.None));
        Assert.Null(handler.Method);
        Assert.DoesNotContain("fake", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RelativeBaseUrl_Throws()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(
            () => new OpenAiCompatibleTranscriber(http, FakeKey, new Uri("v1", UriKind.Relative), "m"));
    }

    [Fact]
    public void Result_ToString_OmitsTranscript()
    {
        var text = new TranscriptionResult("something the user said").ToString();

        Assert.DoesNotContain("something", text, StringComparison.Ordinal);
        Assert.Contains("Chars = 23", text, StringComparison.Ordinal);
    }

    private static async Task<TranscriptionResult> Transcribe(
        FakeHandler handler, string model = "whisper-large-v3-turbo", string? language = null, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, FakeKey, BaseUrl, model, language, timeout);
        return await transcriber.TranscribeAsync(Wav, CancellationToken.None);
    }

    private static async Task<KeyTestResult> TestKey(FakeHandler handler, TimeSpan? timeout = null)
    {
        using var http = new HttpClient(handler);
        return await OpenAiCompatibleTranscriber.TestKeyAsync(http, FakeKey, BaseUrl, CancellationToken.None, timeout);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Part(string? Name, string? FileName, string? MediaType, byte[] Bytes)
    {
        public bool Equals(Part? other) =>
            other is not null && Name == other.Name && FileName == other.FileName
            && MediaType == other.MediaType && Bytes.AsSpan().SequenceEqual(other.Bytes);

        public override int GetHashCode() => HashCode.Combine(Name, FileName, MediaType);
    }

    // Records the request while it is still alive; the transcriber disposes it afterwards.
    private sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public FakeHandler(Func<CancellationToken, HttpResponseMessage> respond)
            : this(token => Task.FromResult(respond(token)))
        {
        }

        public HttpMethod? Method { get; private set; }

        public static FakeHandler Throwing(Exception error) => new(_ => Task.FromException<HttpResponseMessage>(error));

        public Uri? Uri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string? ContentType { get; private set; }

        public List<Part> Parts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                {
                    Parts.Add(new(
                        part.Headers.ContentDisposition?.Name?.Trim('"'),
                        part.Headers.ContentDisposition?.FileName?.Trim('"'),
                        part.Headers.ContentType?.MediaType,
                        await part.ReadAsByteArrayAsync(cancellationToken)));
                }
            }

            return await respond(cancellationToken);
        }
    }

    private sealed class TrackedContent(string body) : StringContent(body, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
