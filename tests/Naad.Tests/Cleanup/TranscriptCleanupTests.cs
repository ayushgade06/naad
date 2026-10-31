using Naad.Cleanup;
using Naad.LanguageModel;

namespace Naad.Tests.Cleanup;

[Collection("Log")]
public sealed class TranscriptCleanupTests : IDisposable
{
    private const string Said = "um we should meet on tuesday";
    private readonly string _cooldownPath = Path.Combine(Path.GetTempPath(), $"naad-cool-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_cooldownPath);

    [Fact]
    public async Task Clean_Off_PastesAsSaid()
    {
        var model = new FakeModel("unused");

        var result = await Cleanup(CleanupLevel.Off, model).CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult(Said, CleanupOutcome.Unchanged), result);
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public async Task Clean_Basic_AppliesRulesWithoutTheModel()
    {
        var model = new FakeModel("unused");

        var result = await Cleanup(CleanupLevel.Basic, model).CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult("We should meet on tuesday.", CleanupOutcome.Basic), result);
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public async Task Clean_AiWithoutAModel_CleansLikeBasic()
    {
        var result = await new TranscriptCleanup(CleanupLevel.Ai, ai: null).CleanAsync(Said, CancellationToken.None);

        Assert.Equal(CleanupOutcome.Basic, result.Outcome);
    }

    [Fact]
    public async Task Clean_Ai_PastesTheModelsText()
    {
        var model = new FakeModel("We should meet on Tuesday.");

        var result = await Cleanup(CleanupLevel.Ai, model).CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult("We should meet on Tuesday.", CleanupOutcome.Cleaned), result);

        // The model gets the text through the instant rules first.
        Assert.EndsWith("<<<\nWe should meet on tuesday.\n>>>", model.Prompts.Single().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_OnlyHesitations_PastesNothingWithoutAskingTheModel()
    {
        var model = new FakeModel("Invented text.");

        var result = await Cleanup(CleanupLevel.Ai, model).CleanAsync("um uh", CancellationToken.None);

        Assert.Equal(new CleanupResult("", CleanupOutcome.Empty), result);
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public async Task Clean_ReplyWithoutCapitalOrStop_GetsThem()
    {
        var result = await Cleanup(CleanupLevel.Ai, new FakeModel("we should meet on Tuesday"))
            .CleanAsync(Said, CancellationToken.None);

        Assert.Equal("We should meet on Tuesday.", result.Text);
    }

    [Fact]
    public async Task Clean_ModelSaysEmpty_PastesNothing()
    {
        var cleanup = Cleanup(CleanupLevel.Ai, new FakeModel("EMPTY"));

        var result = await cleanup.CleanAsync("hmm okay", CancellationToken.None);

        Assert.Equal(new CleanupResult("", CleanupOutcome.Empty), result);
    }

    [Theory]
    [InlineData("Sure! Here is the cleaned text: We should meet on Tuesday.")]
    [InlineData("")]
    public async Task Clean_UnusableReplyAndNoFallback_PastesAsSaid(string reply)
    {
        var result = await Cleanup(CleanupLevel.Ai, new FakeModel(reply)).CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult(Said, CleanupOutcome.FellBack), result);
    }

    [Fact]
    public async Task Clean_ModelFails_PastesAsSaidWithoutTryingTheFallback()
    {
        var fallback = new FakeModel("We should meet on Tuesday.");

        var result = await Cleanup(CleanupLevel.Ai, FakeModel.Failing(CompletionError.ServerError), fallback)
            .CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult(Said, CleanupOutcome.FellBack), result);
        Assert.Empty(fallback.Prompts);
    }

    [Fact]
    public async Task Clean_RateLimited_UsesTheFallbackAndCoolsTheFirstModelDown()
    {
        var first = FakeModel.Failing(CompletionError.RateLimited, TimeSpan.FromMinutes(1));
        var fallback = new FakeModel("We should meet on Tuesday.");
        var cleanup = Cleanup(CleanupLevel.Ai, first, fallback);

        var once = await cleanup.CleanAsync(Said, CancellationToken.None);
        var twice = await cleanup.CleanAsync(Said, CancellationToken.None);

        Assert.Equal(CleanupOutcome.Cleaned, once.Outcome);
        Assert.Equal(CleanupOutcome.Cleaned, twice.Outcome);
        Assert.Single(first.Prompts);
        Assert.Equal(2, fallback.Prompts.Count);
    }

    [Fact]
    public async Task Clean_AnsweredByFirstModel_TriesTheFallback()
    {
        var first = new FakeModel("The capital of France is Paris.");
        var fallback = new FakeModel("Ask Claude what the capital of France is.");

        var result = await Cleanup(CleanupLevel.Ai, first, fallback)
            .CleanAsync("ask claude what the capital of france is", CancellationToken.None);

        Assert.Equal(new CleanupResult("Ask Claude what the capital of France is.", CleanupOutcome.Cleaned), result);
    }

    [Fact]
    public async Task Clean_ModelTooSlow_PastesAsSaidAfterTheBudget()
    {
        var slow = new FakeModel("late") { Delay = Timeout.InfiniteTimeSpan };
        var cleanup = new TranscriptCleanup(
            CleanupLevel.Ai, Ai([new("m", slow)]), budget: TimeSpan.FromMilliseconds(50));

        var result = await cleanup.CleanAsync(Said, CancellationToken.None);

        Assert.Equal(new CleanupResult(Said, CleanupOutcome.FellBack), result);
    }

    [Fact]
    public async Task Clean_Cancelled_Throws()
    {
        var slow = new FakeModel("late") { Delay = Timeout.InfiniteTimeSpan };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Cleanup(CleanupLevel.Ai, slow).CleanAsync(Said, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Clean_OffWithOutputLanguage_TranslatesLiterally()
    {
        var model = new FakeModel("Wir sollten uns am Dienstag treffen.");
        var cleanup = new TranscriptCleanup(
            CleanupLevel.Off, Ai([new("m", model)], outputLanguage: "German"));

        var result = await cleanup.CleanAsync(Said, CancellationToken.None);

        Assert.Equal(CleanupOutcome.Cleaned, result.Outcome);
        Assert.StartsWith("Translate the dictated text into German", model.Prompts.Single().Instructions,
            StringComparison.Ordinal);
    }

    private TranscriptCleanup Cleanup(CleanupLevel level, FakeModel model, FakeModel? fallback = null) =>
        new(level, Ai(fallback is null ? [new("m", model)] : [new("m", model), new("f", fallback)]));

    private AiCleanup Ai(NamedModel[] models, string? outputLanguage = null) => new(
        models,
        new RateLimitCooldowns(_cooldownPath, TimeProvider.System),
        new CleanupOptions(null, "Naad", outputLanguage, Guard: true));

    private sealed class FakeModel(string reply) : ILanguageModel
    {
        private CompletionFailure? _failure;

        public List<ChatPrompt> Prompts { get; } = [];

        public TimeSpan Delay { get; init; } = TimeSpan.Zero;

        public static FakeModel Failing(CompletionError error, TimeSpan? wait = null) =>
            new("") { _failure = new(error, 0, wait) };

        public async Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Prompts.Add(prompt);
            if (Delay != TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return _failure is { } failure ? new("", failure) : new(reply);
        }
    }
}
