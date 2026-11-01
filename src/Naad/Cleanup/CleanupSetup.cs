using System.Diagnostics;
using Naad.Context;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Settings;
using Naad.Snippets;
using Naad.Transcription;

namespace Naad.Cleanup;

/// <param name="Reply">The model's reply, as it came; empty on failure. User content: shown, never logged.</param>
/// <param name="Verdict">What the reply check made of it, or null on failure.</param>
/// <param name="Milliseconds">How long the reply took.</param>
/// <param name="Sent">The prompt that was sent.</param>
/// <param name="Failure">Why it failed, or null.</param>
internal sealed record PromptTest(
    string Reply, ReplyVerdict? Verdict, long Milliseconds, ChatPrompt Sent, CompletionFailure? Failure)
{
    // The generated ToString would print the reply and the prompt.
    public override string ToString() => $"PromptTest {{ Verdict = {Verdict}, Milliseconds = {Milliseconds} }}";
}

/// <param name="Model">The model's id, or <see cref="CleanupSetup.LocalModelName"/>.</param>
/// <param name="Result">What the test found.</param>
internal sealed record ModelTest(string Model, PromptTest Result);

/// <summary>Builds the language model and the cleanup step from the settings.</summary>
internal static class CleanupSetup
{
    // The summary runs while the user speaks; one that takes longer than a long sentence is no use.
    private static readonly TimeSpan ContextTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The context model's id in the cloud: the chosen one, or the service's suggestion.</summary>
    public static string ContextModelName(AppSettings s) =>
        s.ContextModelName
        ?? (CloudServices.ContextModels(s.LanguageModelService) is [var suggested, ..]
            ? suggested
            : s.LanguageModelName);

    /// <summary>
    /// What sums up the user's activity while they speak, or null when no language model runs. In
    /// the cloud it is its own model; on this PC, <paramref name="model"/> itself.
    /// </summary>
    public static ContextSummarizer? CreateContextSummarizer(
        ILanguageModel? model,
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature,
        Func<string?> customPrompt)
    {
        return ContextModel(model, http, keyFor, s, temperature) is { } contextModel
            ? new(contextModel, customPrompt, ContextTimeout)
            : null;
    }

    /// <summary>
    /// Sends the context prompt for <paramref name="sample"/>, as text near the cursor in an email,
    /// to the context model once, for Settings to show what came back.
    /// </summary>
    public static async Task<PromptTest> TestContextAsync(
        ILanguageModel? model,
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature,
        string? instructions,
        string sample,
        CancellationToken token)
    {
        var prompt = ContextPrompt.Build(instructions, new("outlook", sample, null, null));
        if (ContextModel(model, http, keyFor, s, temperature) is not { } contextModel)
        {
            return new("", null, 0, prompt, null);
        }

        var started = Stopwatch.GetTimestamp();
        CompletionResult result;
        try
        {
            result = await contextModel.CompleteAsync(prompt, token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            result = new("", new(CompletionError.Timeout));
        }

        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Log.Info(LogEvent.ContextPromptTested, ("failed", result.Failure is null ? 0 : 1), ("reply_ms", milliseconds));
        var verdict = ContextPrompt.TwoSentences(result.Text) is null ? ReplyVerdict.Empty : ReplyVerdict.Accepted;
        return result.Failure is { } failure
            ? new("", null, milliseconds, prompt, failure)
            : new(result.Text, verdict, milliseconds, prompt, null);
    }

    // In the cloud, a model of its own; on this PC, the model that cleans up.
    private static ILanguageModel? ContextModel(
        ILanguageModel? model,
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature) =>
        model is null ? null
        : s.LanguageModelProvider == LanguageModelProvider.Cloud ? new OpenAiChatLanguageModel(
            http,
            keyFor(s.LanguageModelService),
            CloudServices.For(s.LanguageModelService).BaseUrl ?? s.LanguageModelBaseUrl,
            ContextModelName(s),
            temperature,
            ContextTimeout)
        : model;

    /// <summary>
    /// The summary to start with each recording, or null when context isn't read for AI cleanup
    /// or no cloud model runs. The model on this PC answers one prompt at a time, so a summary
    /// would make cleanup wait for it; there, cleanup gets the app and nearby text directly.
    /// </summary>
    public static Func<Task<DictationContext>, CancellationToken, Task<ContextSummary>>? Summary(
        ILanguageModel? model, HttpClient http, Func<CloudService, string?> keyFor, AppSettings s,
        TemperatureSupport temperature) =>
        s.Context && s.Cleanup == CleanupLevel.Ai && s.LanguageModelProvider == LanguageModelProvider.Cloud
            && CreateContextSummarizer(model, http, keyFor, s, temperature, () => s.ContextPrompt) is { } summarizer
            ? summarizer.SummarizeAsync
            : null;

    /// <summary>How a test names the model on this PC.</summary>
    public const string LocalModelName = "Model on this PC";

    /// <summary>
    /// The models <b>Test models</b> asks, in order: the cloud model, its fallback and the context
    /// model, each once, or the model on this PC.
    /// </summary>
    public static IReadOnlyList<string> ModelsToTest(AppSettings s) =>
        s.LanguageModelProvider != LanguageModelProvider.Cloud
            ? [LocalModelName]
            : [.. new[] { s.LanguageModelName, s.LanguageModelFallbackName, ContextModelName(s) }
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct()
                .Cast<string>()];

    /// <summary>
    /// Sends the sample to each of <see cref="ModelsToTest"/> in turn: <paramref name="model"/> is
    /// the one in use, and a fallback is built from the saved settings.
    /// </summary>
    public static async Task<IReadOnlyList<ModelTest>> TestModelsAsync(
        ILanguageModel? model,
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature,
        string? instructions,
        string sample,
        CancellationToken token)
    {
        var results = new List<ModelTest>();
        foreach (var name in ModelsToTest(s))
        {
            var tested = name == LocalModelName || name == s.LanguageModelName
                ? model
                : new OpenAiChatLanguageModel(
                    http,
                    keyFor(s.LanguageModelService),
                    CloudServices.For(s.LanguageModelService).BaseUrl ?? s.LanguageModelBaseUrl,
                    name,
                    temperature,
                    TimeSpan.FromSeconds(s.LanguageModelTimeoutSeconds));
            results.Add(new(name, await TestAsync(tested, s, instructions, sample, token)));
        }

        return results;
    }

    /// <summary>
    /// Whether the language model must be replaced: its provider, model, service, address or
    /// time limit changed. Replacing a model on this PC unloads it, so nothing else does.
    /// </summary>
    public static bool ModelChanged(AppSettings before, AppSettings after) =>
        before.LanguageModelProvider != after.LanguageModelProvider
        || before.LocalLanguageModel != after.LocalLanguageModel
        || before.LanguageModelService != after.LanguageModelService
        || before.LanguageModelBaseUrl != after.LanguageModelBaseUrl
        || before.LanguageModelName != after.LanguageModelName
        || before.LanguageModelTimeoutSeconds != after.LanguageModelTimeoutSeconds;

    // The model may be loading for the first time, compiling its shaders; a person is watching.
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The language model the settings choose, or null when it can't run yet. A cloud model falls
    /// back to the model on this PC when offline, if that is downloaded.
    /// </summary>
    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="keyFor">A service's API key, or null.</param>
    /// <param name="s">The settings.</param>
    /// <param name="temperature">Which cloud models refused a temperature.</param>
    /// <param name="offline">Called when the model on this PC answered because the PC was offline.</param>
    public static ILanguageModel? CreateModel(
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature,
        Action offline)
    {
        var file = LanguageModelFiles.For(s.LocalLanguageModel);
        var key = keyFor(s.LanguageModelService);
        var url = CloudServices.For(s.LanguageModelService).BaseUrl ?? s.LanguageModelBaseUrl;
        ILanguageModel Local() => new LocalLanguageModel(file.Path, TimeProvider.System);
        ILanguageModel Cloud(string model) => new OpenAiChatLanguageModel(
            http, key, url, model, temperature, TimeSpan.FromSeconds(s.LanguageModelTimeoutSeconds));
        return LanguageModelSelection.Choose(
            s.LanguageModelProvider,
            key is not null,
            s.LanguageModelService == CloudService.Custom && url.IsLoopback,
            file.IsReady()) switch
        {
            LanguageModelProvider.Cloud when file.IsReady() => new OfflineFallbackLanguageModel(
                Cloud(s.LanguageModelName), () => file.IsReady() ? Local() : null, Reachability.IsOnline, offline),
            LanguageModelProvider.Cloud => Cloud(s.LanguageModelName),
            LanguageModelProvider.Local => Local(),
            _ => null,
        };
    }

    /// <summary>
    /// The cleanup step for <paramref name="s"/>, using <paramref name="model"/> and, in the
    /// cloud, the fallback model when one is set.
    /// </summary>
    public static TranscriptCleanup Create(
        ILanguageModel? model,
        HttpClient http,
        Func<CloudService, string?> keyFor,
        AppSettings s,
        TemperatureSupport temperature,
        RateLimitCooldowns cooldowns,
        IReadOnlyList<Snippet>? snippets = null)
    {
        var budget = TimeSpan.FromSeconds(s.LanguageModelTimeoutSeconds);
        if (model is null)
        {
            return new(s.Cleanup, ai: null, budget, snippets, s.PressEnterCommand);
        }

        // Rate limits are per cloud model; a model on this PC has none.
        var cloud = s.LanguageModelProvider == LanguageModelProvider.Cloud;
        var models = new List<NamedModel> { new(cloud ? s.LanguageModelName : "local", model) };
        if (cloud && s.LanguageModelFallbackName is { } fallback && fallback != s.LanguageModelName)
        {
            var url = CloudServices.For(s.LanguageModelService).BaseUrl ?? s.LanguageModelBaseUrl;
            models.Add(new(fallback, new OpenAiChatLanguageModel(
                http, keyFor(s.LanguageModelService), url, fallback, temperature, budget)));
        }

        var options = new CleanupOptions(
            s.CleanupPrompt, s.Vocabulary, s.OutputLanguage, s.InstructionGuard, SpokenLanguage: s.Language);
        return new(s.Cleanup, new AiCleanup(models, cooldowns, options), budget, snippets, s.PressEnterCommand);
    }

    /// <summary>
    /// Sends <paramref name="sample"/> with <paramref name="instructions"/> to
    /// <paramref name="model"/> once, for Settings to show what came back and how long it took.
    /// </summary>
    public static async Task<PromptTest> TestAsync(
        ILanguageModel? model, AppSettings s, string? instructions, string sample, CancellationToken token)
    {
        var prompt = CleanupPrompt.Clean(
            instructions, s.Vocabulary, s.OutputLanguage, sample, spokenLanguage: s.Language);
        // Neither a verdict nor a failure: there is no model to ask.
        if (model is null)
        {
            return new("", null, 0, prompt, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TestTimeout);
        var started = Stopwatch.GetTimestamp();
        CompletionResult result;
        try
        {
            result = await model.CompleteAsync(prompt, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            result = new("", new(CompletionError.Timeout));
        }

        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Log.Info(LogEvent.CleanupPromptTested, ("failed", result.Failure is null ? 0 : 1), ("reply_ms", milliseconds));
        return result.Failure is { } failure
            ? new("", null, milliseconds, prompt, failure)
            : new(
                result.Text,
                CleanupReplyCheck.Check(sample, result.Text, s.OutputLanguage is not null, s.InstructionGuard).Verdict,
                milliseconds,
                prompt,
                null);
    }
}
