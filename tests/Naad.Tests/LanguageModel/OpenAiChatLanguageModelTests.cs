using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

// Every test runs against a fake HttpMessageHandler; nothing here touches the network.
[Collection("Log")]
public sealed class OpenAiChatLanguageModelTests : IDisposable
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";
    private static readonly Uri BaseUrl = new("https://llm.example.test/openai/v1");
    private static readonly ChatPrompt Prompt = new("Clean this up.", "um hello there", MaxTokens: 64);
    private readonly string _temperaturePath = Path.Combine(Path.GetTempPath(), $"naad-temp-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_temperaturePath);

    [Fact]
    public async Task Complete_Ok_PostsChatRequestAndReturnsTheReply()
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, Choice("Hello there.")));

        var result = await Complete(handler, model: "llama-3.1-8b-instant");

        Assert.Equal(new CompletionResult("Hello there."), result);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://llm.example.test/openai/v1/chat/completions", handler.Uri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", FakeKey), handler.Authorization);
        var body = handler.Bodies.Single().RootElement;
        Assert.Equal("llama-3.1-8b-instant", body.GetProperty("model").GetString());
        Assert.Equal(0, body.GetProperty("temperature").GetInt32());
        Assert.Equal(64, body.GetProperty("max_tokens").GetInt32());
        Assert.False(body.GetProperty("stream").GetBoolean());
        Assert.False(body.TryGetProperty("reasoning_effort", out _));
        Assert.Collection(
            body.GetProperty("messages").EnumerateArray(),
            m => Assert.Equal(("system", "Clean this up."), (Role(m), Content(m))),
            m => Assert.Equal(("user", "um hello there"), (Role(m), Content(m))));
    }

    [Fact]
    public async Task Complete_ReasoningModel_SendsLowestReasoningAndStripsThinking()
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, Choice("<think>hmm</think>Hello.")));

        var result = await Complete(handler, model: "gpt-5-mini");

        Assert.Equal("Hello.", result.Text);
        var body = handler.Bodies.Single().RootElement;
        Assert.Equal("minimal", body.GetProperty("reasoning_effort").GetString());
        Assert.Equal(64, body.GetProperty("max_completion_tokens").GetInt32());
    }

    [Fact]
    public async Task Complete_NoKey_SendsNoAuthorization()
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, Choice("hi")));
        using var http = new HttpClient(handler);
        var model = new OpenAiChatLanguageModel(
            http, null, new("http://localhost:11434/v1"), "llama3.2", Temperature());

        await model.CompleteAsync(Prompt, CancellationToken.None);

        Assert.Null(handler.Authorization);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "Unauthorized")]
    [InlineData(HttpStatusCode.NotFound, "Rejected")]
    [InlineData(HttpStatusCode.BadRequest, "Rejected")]
    [InlineData(HttpStatusCode.BadGateway, "ServerError")]
    public async Task Complete_ErrorStatus_FailsWithMappedError(HttpStatusCode status, string error)
    {
        var expected = Enum.Parse<CompletionError>(error);
        using var handler = new FakeHandler(_ => Reply(status, """{ "error": { "message": "secret echo" } }"""));

        var result = await Complete(handler);

        Assert.Equal(new CompletionResult("", new(expected, (int)status)), result);
    }

    [Fact]
    public async Task Complete_RateLimited_ReportsTheWaitFromTheHeaders()
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = Reply(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.Add("retry-after", "7");
            return response;
        });

        var result = await Complete(handler);

        Assert.Equal(new CompletionFailure(CompletionError.RateLimited, 429, TimeSpan.FromSeconds(7)), result.Failure);
    }

    [Fact]
    public async Task Complete_TemperatureRefused_SendsAgainWithoutItAndRemembers()
    {
        var calls = 0;
        using var handler = new FakeHandler(_ => ++calls == 1
            ? Reply(
                HttpStatusCode.BadRequest,
                """{ "error": { "param": "temperature", "code": "unsupported_value" } }""")
            : Reply(HttpStatusCode.OK, Choice("Hello.")));
        var temperature = Temperature();

        var result = await Complete(handler, temperature: temperature);

        Assert.Equal("Hello.", result.Text);
        Assert.True(handler.Bodies[0].RootElement.TryGetProperty("temperature", out _));
        Assert.False(handler.Bodies[1].RootElement.TryGetProperty("temperature", out _));
        Assert.True(temperature.IsRefused(BaseUrl, "m"));
    }

    [Fact]
    public async Task Complete_TemperatureRefusedEarlier_LeavesItOut()
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, Choice("Hello.")));
        var temperature = Temperature();
        temperature.MarkRefused(BaseUrl, "m");

        await Complete(handler, temperature: temperature);

        Assert.False(handler.Bodies.Single().RootElement.TryGetProperty("temperature", out _));
    }

    [Theory]
    [InlineData("""{ "choices": [] }""")]
    [InlineData("""{ "choices": [ { "message": { "content": null } } ] }""")]
    [InlineData("""[1, 2]""")]
    [InlineData("not json")]
    public async Task Complete_UnexpectedBody_FailsAsMalformed(string body)
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, body));

        var result = await Complete(handler);

        Assert.Equal(new CompletionResult("", new(CompletionError.MalformedResponse)), result);
    }

    [Fact]
    public async Task Complete_ConnectionRefused_RetriesOnceThenFailsAsNetwork()
    {
        var calls = 0;
        using var handler = new FakeHandler(_ =>
        {
            calls++;
            return Task.FromException<HttpResponseMessage>(new HttpRequestException(HttpRequestError.ConnectionError));
        });

        var result = await Complete(handler);

        Assert.Equal(2, calls);
        Assert.Equal(CompletionError.Network, result.Failure?.Error);
    }

    [Fact]
    public async Task Complete_NoAnswerInTime_FailsAsTimeout()
    {
        using var handler = new FakeHandler(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Reply(HttpStatusCode.OK, Choice("late"));
        });
        using var http = new HttpClient(handler);
        var model = new OpenAiChatLanguageModel(
            http, FakeKey, BaseUrl, "m", Temperature(), timeout: TimeSpan.FromMilliseconds(50));

        var result = await model.CompleteAsync(Prompt, CancellationToken.None);

        Assert.Equal(new CompletionResult("", new(CompletionError.Timeout)), result);
    }

    [Fact]
    public async Task Complete_AlreadyCancelled_ThrowsWithoutSending()
    {
        using var handler = new FakeHandler(_ => Reply(HttpStatusCode.OK, Choice("hi")));
        using var http = new HttpClient(handler);
        var model = new OpenAiChatLanguageModel(http, FakeKey, BaseUrl, "m", Temperature());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.CompleteAsync(Prompt, new CancellationToken(canceled: true)));
        Assert.Empty(handler.Bodies);
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("")]
    public void Constructor_InvalidKey_Throws(string key)
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(() => new OpenAiChatLanguageModel(http, key, BaseUrl, "m", Temperature()));
    }

    [Fact]
    public void Constructor_PlainHttpToAnotherMachine_Throws()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(
            () => new OpenAiChatLanguageModel(http, FakeKey, new("http://llm.example.test/v1"), "m", Temperature()));
    }

    [Fact]
    public void ToString_Prompt_NeverShowsTheText()
    {
        Assert.DoesNotContain("hello", Prompt.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", new CompletionResult("Hello there").ToString(), StringComparison.Ordinal);
    }

    private TemperatureSupport Temperature() => new(_temperaturePath, TimeProvider.System);

    private async Task<CompletionResult> Complete(
        FakeHandler handler, string model = "m", TemperatureSupport? temperature = null)
    {
        using var http = new HttpClient(handler);
        return await new OpenAiChatLanguageModel(http, FakeKey, BaseUrl, model, temperature ?? Temperature())
            .CompleteAsync(Prompt, CancellationToken.None);
    }

    private static string Choice(string content) =>
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string? Role(JsonElement message) => message.GetProperty("role").GetString();

    private static string? Content(JsonElement message) => message.GetProperty("content").GetString();

    private sealed class FakeHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public FakeHandler(Func<CancellationToken, HttpResponseMessage> respond)
            : this(token => Task.FromResult(respond(token)))
        {
        }

        public HttpMethod? Method { get; private set; }

        public Uri? Uri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public List<JsonDocument> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)));
            return await respond(cancellationToken);
        }
    }
}
