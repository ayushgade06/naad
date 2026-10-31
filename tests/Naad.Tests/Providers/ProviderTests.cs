using System.Net;
using System.Net.Http.Headers;
using Naad.Providers;
using Naad.Settings;
using Naad.Transcription;

namespace Naad.Tests.Providers;

// Fake HTTP handlers only; nothing here touches the network.
[Collection("Log")]
public sealed class ProviderTests : IDisposable
{
    private const string FakeKey = "fake-api-key-0123456789";
    private readonly string _folder = Directory.CreateTempSubdirectory("naad-keys-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData("https://api.groq.com/openai/v1", "Groq")]
    [InlineData("https://API.OPENAI.COM/v1", "OpenAI")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "Gemini")]
    [InlineData("https://api.x.ai/v1", "XAI")]
    [InlineData("https://api.deepgram.com/v1", "Deepgram")]
    [InlineData("http://localhost:11434/v1", "Custom")]
    public void FromUrl_KnownHost_PicksItsService(string url, string service)
    {
        Assert.Equal(service, CloudServices.FromUrl(new(url)).ToString());
    }

    [Fact]
    public void Presets_EveryService_HasWhatItsTabsNeed()
    {
        foreach (var service in CloudServices.ForTranscription.Where(s => s != CloudService.Custom))
        {
            Assert.NotEmpty(CloudServices.For(service).TranscriptionModels);
        }

        foreach (var service in CloudServices.ForLanguageModel.Where(s => s != CloudService.Custom))
        {
            Assert.True(CloudServices.For(service).LanguageModels.Count >= 2);
            Assert.NotNull(CloudServices.For(service).BaseUrl);
        }

        Assert.Equal("NAAD_GROQ_API_KEY", CloudServices.OverrideVariable(CloudService.Groq));
    }

    [Fact]
    public void Resolve_LegacyKey_MovesToItsServiceOnce()
    {
        var legacy = Path.Combine(_folder, "api-key.dpapi");
        Assert.Null(new SecretStore(legacy).Save(FakeKey));
        var keys = new ServiceKeys(Path.Combine(_folder, "keys"), legacy, CloudService.OpenAI, _ => null);

        Assert.Equal(FakeKey, keys.Resolve(CloudService.OpenAI).Key);
        Assert.Null(keys.Resolve(CloudService.Groq).Key);
        Assert.False(File.Exists(legacy));
    }

    [Fact]
    public void Save_PerService_KeepsKeysApartAndHonoursTheOverride()
    {
        var keys = new ServiceKeys(
            _folder, Path.Combine(_folder, "none.dpapi"), CloudService.Groq,
            name => name == "NAAD_DEEPGRAM_API_KEY" ? "fake-override-key" : null);

        Assert.Null(keys.Save(CloudService.Groq, FakeKey));

        Assert.Equal(FakeKey, keys.Resolve(CloudService.Groq).Key);
        Assert.Null(keys.Resolve(CloudService.OpenAI).Key);
        Assert.Equal(("fake-override-key", ApiKeySource.Environment), (
            keys.Resolve(CloudService.Deepgram).Key, keys.Resolve(CloudService.Deepgram).Source));
    }

    [Theory]
    [InlineData("Groq", "https://api.groq.com/openai/v1/models", "Authorization", "Bearer")]
    [InlineData("Deepgram", "https://api.deepgram.com/v1/projects", "Authorization", "Token")]
    [InlineData("Anthropic", "https://api.anthropic.com/v1/models", "x-api-key", null)]
    [InlineData("Gemini", "https://generativelanguage.googleapis.com/v1beta/models", "x-goog-api-key", null)]
    [InlineData("Custom", "https://llm.example.test/v1/models", "Authorization", "Bearer")]
    public async Task KeyCheck_Service_ListsWithItsOwnHeader(string service, string url, string header, string? scheme)
    {
        using var handler = new Recorder(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);

        var result = await ServiceKeyCheck.TestAsync(
            http,
            Enum.Parse<CloudService>(service),
            FakeKey,
            new("https://llm.example.test/v1"),
            CancellationToken.None);

        Assert.Equal(KeyTestOutcome.Accepted, result.Outcome);
        Assert.Equal(url, handler.Requests.Single().RequestUri!.AbsoluteUri);
        var request = handler.Requests.Single();
        if (scheme is null)
        {
            Assert.Equal(FakeKey, request.Headers.GetValues(header).Single());
        }
        else
        {
            Assert.Equal(new AuthenticationHeaderValue(scheme, FakeKey), request.Headers.Authorization);
        }
    }

    [Fact]
    public async Task KeyCheck_Rejected_FailsAsUnauthorized()
    {
        using var handler = new Recorder(HttpStatusCode.Unauthorized, "{}");
        using var http = new HttpClient(handler);

        var result = await ServiceKeyCheck.TestAsync(
            http, CloudService.OpenAI, FakeKey, new("https://unused.example.test"), CancellationToken.None);

        Assert.Equal(TranscriptionError.Unauthorized, result.Failure?.Error);
    }

    [Fact]
    public async Task Deepgram_Ok_PostsRawWavWithTokenAndReadsTheTranscript()
    {
        using var handler = new Recorder(
            HttpStatusCode.OK,
            """{ "results": { "channels": [ { "alternatives": [ { "transcript": " Hello there. " } ] } ] } }""");
        using var http = new HttpClient(handler);
        var transcriber = new DeepgramTranscriber(
            http, FakeKey, "nova-3", "en", "Naad, Priya", TimeSpan.FromSeconds(5));

        var result = await transcriber.TranscribeAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(new TranscriptionResult("Hello there."), result);
        var request = handler.Requests.Single();
        Assert.Equal(
            "https://api.deepgram.com/v1/listen?model=nova-3&smart_format=true&language=en&keyterm=Naad&keyterm=Priya",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Token", FakeKey), request.Headers.Authorization);
        Assert.Equal("audio/wav", handler.ContentTypes.Single());
        Assert.Equal([1, 2, 3], handler.Bodies.Single());
    }

    [Fact]
    public void Deepgram_OlderModel_SendsKeywordsNotKeyterms()
    {
        Assert.Equal(
            "https://api.deepgram.com/v1/listen?model=nova-2&smart_format=true&detect_language=true&keywords=Naad",
            DeepgramTranscriber.Endpoint("nova-2", null, "Naad").AbsoluteUri);
    }

    [Fact]
    public void Deepgram_NoLanguage_DetectsIt()
    {
        Assert.Equal(
            "https://api.deepgram.com/v1/listen?model=nova-2&smart_format=true&detect_language=true",
            DeepgramTranscriber.Endpoint("nova-2", null, null).AbsoluteUri);
    }

    [Theory]
    [InlineData("""{ "candidates": [ { "content": { "parts": [ { "text": "Hello " }, { "text": "there." } ] } } ] }""",
        "Hello there.")]
    [InlineData("""{ "candidates": [] }""", "")]
    public async Task Gemini_Ok_SendsInlineAudioAndJoinsTheParts(string body, string expected)
    {
        using var handler = new Recorder(HttpStatusCode.OK, body);
        using var http = new HttpClient(handler);
        var transcriber = new GeminiTranscriber(
            http, FakeKey, "gemini-2.5-flash", "hi", "Naad", TimeSpan.FromSeconds(5));

        var result = await transcriber.TranscribeAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(new TranscriptionResult(expected), result);
        var request = handler.Requests.Single();
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(FakeKey, request.Headers.GetValues("x-goog-api-key").Single());
        var sent = System.Text.Encoding.UTF8.GetString(handler.Bodies.Single());
        Assert.Contains("\"data\":\"AQID\"", sent, StringComparison.Ordinal);
        Assert.Contains("\\u0022hi\\u0022", sent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini_RecordingTooLarge_FailsWithoutSending()
    {
        using var handler = new Recorder(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);
        var transcriber = new GeminiTranscriber(http, FakeKey, "gemini-2.5-flash", null, null, TimeSpan.FromSeconds(5));

        var tooLarge = new byte[GeminiTranscriber.MaxWavBytes + 1];

        var result = await transcriber.TranscribeAsync(tooLarge, CancellationToken.None);

        Assert.Equal(TranscriptionError.TooLarge, result.Failure?.Error);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task XAi_UsesItsOwnPath()
    {
        using var handler = new Recorder(HttpStatusCode.OK, """{ "text": "hi" }""");
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(
            http, FakeKey, new("https://api.x.ai/v1"), "grok-stt", path: "stt");

        await transcriber.TranscribeAsync([1], CancellationToken.None);

        Assert.Equal("https://api.x.ai/v1/stt", handler.Requests.Single().RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Custom_ServerOnThisPc_SendsNoKey()
    {
        using var handler = new Recorder(HttpStatusCode.OK, """{ "text": "hi" }""");
        using var http = new HttpClient(handler);
        var transcriber = new OpenAiCompatibleTranscriber(http, null, new("http://localhost:8000/v1"), "m");

        await transcriber.TranscribeAsync([1], CancellationToken.None);

        Assert.Null(handler.Requests.Single().Headers.Authorization);
    }

    [Fact]
    public void Custom_ServerElsewhereWithoutKey_Throws()
    {
        using var http = new HttpClient();

        Assert.Throws<ArgumentException>(
            () => new OpenAiCompatibleTranscriber(http, null, new("https://stt.example.test/v1"), "m"));
    }

    // Records requests and their bodies while they are alive.
    private sealed class Recorder(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<byte[]> Bodies { get; } = [];

        public List<string?> ContentTypes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                ContentTypes.Add(request.Content.Headers.ContentType?.MediaType);
                Bodies.Add(await request.Content.ReadAsByteArrayAsync(cancellationToken));
            }

            return new(status) { Content = new StringContent(body) };
        }
    }
}
