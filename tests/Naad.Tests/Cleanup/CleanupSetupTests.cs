using Naad.Cleanup;
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

        Assert.IsType<OpenAiChatLanguageModel>(CleanupSetup.CreateModel(http, FakeKey, s, Temperature()));
    }

    [Fact]
    public void CreateModel_CloudWithoutKey_IsNone()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with { LanguageModelProvider = LanguageModelProvider.Cloud };

        Assert.Null(CleanupSetup.CreateModel(http, null, s, Temperature()));
    }

    [Fact]
    public void CreateModel_ServerOnThisPcWithoutKey_IsTheChatModel()
    {
        using var http = new HttpClient();
        var s = AppSettings.Defaults with
        {
            LanguageModelProvider = LanguageModelProvider.Cloud,
            LanguageModelBaseUrl = new("http://localhost:11434/v1"),
        };

        Assert.NotNull(CleanupSetup.CreateModel(http, null, s, Temperature()));
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
            new Reply("Sure! Here you go."), http, FakeKey, s, Temperature(), Cooldowns());

        var result = await cleanup.CleanAsync("hello there", CancellationToken.None);

        Assert.Equal(new CleanupResult("hello there", CleanupOutcome.FellBack), result);
    }

    [Fact]
    public async Task Create_NoModel_CleansLikeBasic()
    {
        using var http = new HttpClient();
        var cleanup = CleanupSetup.Create(null, http, null, AppSettings.Defaults, Temperature(), Cooldowns());

        Assert.Equal(CleanupOutcome.Basic, (await cleanup.CleanAsync("um hi", CancellationToken.None)).Outcome);
    }

    private TemperatureSupport Temperature() => new(Path.Combine(_folder, "t.json"), TimeProvider.System);

    private RateLimitCooldowns Cooldowns() => new(Path.Combine(_folder, "c.json"), TimeProvider.System);

    private sealed class Reply(string text) : ILanguageModel
    {
        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken) =>
            Task.FromResult(new CompletionResult(text));
    }
}
