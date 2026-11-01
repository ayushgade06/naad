using Naad.Cleanup;
using Naad.Context;
using Naad.LanguageModel;
using Naad.Settings;

namespace Naad.Tests.Cleanup;

[Collection("Log")]
public sealed class CleanupSetupTests : IDisposable
{
    private const string FakeKey = "fake-api-key-0123456789";
    private readonly string _folder = Directory.CreateTempSubdirectory("naad-setup-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void CreateModel_CloudWithKey_IsTheChatModel()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with { LanguageModelProvider = LanguageModelProvider.Cloud };

        var model = CleanupSetup.CreateModel(http, _ => FakeKey, s, Temperature(), () => { });

        // Wrapped for offline use when this PC has the local model downloaded.
        Assert.True(model is OpenAiChatLanguageModel or OfflineFallbackLanguageModel);
    }

    [Fact]
    public void CreateModel_CloudWithoutKey_IsNone()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with { LanguageModelProvider = LanguageModelProvider.Cloud };

        Assert.Null(CleanupSetup.CreateModel(http, _ => null, s, Temperature(), () => { }));
    }

    [Fact]
    public void CreateModel_ServerOnThisPcWithoutKey_IsTheChatModel()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Cloud,
            LanguageModelService = Naad.Providers.CloudService.Custom,
            LanguageModelBaseUrl = new("http://localhost:11434/v1"),
        };

        Assert.NotNull(CleanupSetup.CreateModel(http, _ => null, s, Temperature(), () => { }));
    }

    [Fact]
    public async Task TestAsync_NoModel_SaysSoWithoutVerdictOrFailure()
    {
        var test = await CleanupSetup.TestAsync(null, AppSettings.Defaults, null, "um hi", CancellationToken.None);

        Assert.Equal((null, null), (test.Verdict, test.Failure));
        Assert.Equal(CleanupPrompt.DefaultInstructions, test.Sent.Instructions);
    }

    [Fact]
    public async Task TestAsync_Model_ReturnsTheReplyAndItsVerdict()
    {
        var test = await CleanupSetup.TestAsync(
            new Reply("Hi."), AppSettings.Defaults, "Be brief.", "um hi", CancellationToken.None);

        Assert.Equal(("Hi.", ReplyVerdict.Accepted), (test.Reply, test.Verdict));
        Assert.Equal("Be brief.", test.Sent.Instructions);
    }

    [Fact]
    public async Task Create_FallbackSetForANonCloudModel_IsNotUsed()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with { Cleanup = CleanupLevel.Ai, LanguageModelFallbackName = "other" };
        var cleanup = CleanupSetup.Create(
            new Reply("Sure! Here you go."), http, _ => FakeKey, s, Temperature(), Cooldowns());

        var result = await cleanup.CleanAsync("hello there", DictationContext.None, CancellationToken.None);

        Assert.Equal(new CleanupResult("hello there", CleanupOutcome.FellBack), result);
    }

    [Fact]
    public async Task Create_NoModel_CleansLikeBasic()
    {
        using var http = new HttpClient();
        var cleanup = CleanupSetup.Create(null, http, _ => null, AppSettings.Defaults, Temperature(), Cooldowns());

        Assert.Equal(CleanupOutcome.Basic, (await cleanup.CleanAsync("um hi", DictationContext.None,
            CancellationToken.None)).Outcome);
    }

    private TemperatureSupport Temperature() => new(Path.Combine(_folder, "t.json"), TimeProvider.System);

    private RateLimitCooldowns Cooldowns() => new(Path.Combine(_folder, "c.json"), TimeProvider.System);

    private sealed class Reply(string text) : ILanguageModel
    {
        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken) =>
            Task.FromResult(new CompletionResult(text));
    }

    [Fact]
    public void ModelsToTest_CloudWithFallback_TestsBoth()
    {
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Cloud,
            LanguageModelName = "synthetic-main",
            LanguageModelFallbackName = "synthetic-fallback",
            ContextModelName = "synthetic-context",
        };

        Assert.Equal(["synthetic-main", "synthetic-fallback", "synthetic-context"], CleanupSetup.ModelsToTest(s));
    }

    [Fact]
    public void ModelsToTest_FallbackSameAsMain_TestsItOnce()
    {
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Cloud,
            LanguageModelName = "synthetic-main",
            LanguageModelFallbackName = "synthetic-main",
            ContextModelName = "synthetic-main",
        };

        Assert.Equal(["synthetic-main"], CleanupSetup.ModelsToTest(s));
    }

    [Fact]
    public void ModelsToTest_OnThisPc_TestsTheLocalModelOnly()
    {
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Local,
            LanguageModelFallbackName = "synthetic-fallback",
        };

        Assert.Equal([CleanupSetup.LocalModelName], CleanupSetup.ModelsToTest(s));
    }

    [Fact]
    public async Task TestModelsAsync_CloudWithFallback_AsksTheFallbackByName()
    {
        var models = new List<string>();
        using var http = new HttpClient(new ModelEcho(models));
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Cloud,
            LanguageModelName = "synthetic-main",
            LanguageModelFallbackName = "synthetic-fallback",
            ContextModelName = "synthetic-main",
        };
        var main = new OpenAiChatLanguageModel(http, FakeKey, s.LanguageModelBaseUrl, "synthetic-main", Temperature());

        var results = await CleanupSetup.TestModelsAsync(
            main, http, _ => FakeKey, s, Temperature(), null, "synthetic sample", CancellationToken.None);

        Assert.Equal(["synthetic-main", "synthetic-fallback"], results.Select(r => r.Model));
        Assert.Equal(["synthetic-main", "synthetic-fallback"], models);
        Assert.All(results, r => Assert.Null(r.Result.Failure));
    }

    // Answers every chat request with the sample tidied, and notes which model it was for.
    private sealed class ModelEcho(List<string> models) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = System.Text.Json.JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync(cancellationToken));
            models.Add(body.RootElement.GetProperty("model").GetString()!);
            return new(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "choices": [ { "message": { "content": "Synthetic sample." } } ] }"""),
            };
        }
    }
}
