using System.Diagnostics;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Settings;
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

/// <summary>Builds the language model and the cleanup step from the settings.</summary>
internal static class CleanupSetup
{
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
        RateLimitCooldowns cooldowns)
    {
        var budget = TimeSpan.FromSeconds(s.LanguageModelTimeoutSeconds);
        if (model is null)
        {
            return new(s.Cleanup, ai: null, budget);
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

        var options = new CleanupOptions(s.CleanupPrompt, s.Vocabulary, s.OutputLanguage, s.InstructionGuard);
        return new(s.Cleanup, new AiCleanup(models, cooldowns, options), budget);
    }

    /// <summary>
    /// Sends <paramref name="sample"/> with <paramref name="instructions"/> to
    /// <paramref name="model"/> once, for Settings to show what came back and how long it took.
    /// </summary>
    public static async Task<PromptTest> TestAsync(
        ILanguageModel? model, AppSettings s, string? instructions, string sample, CancellationToken token)
    {
        var prompt = CleanupPrompt.Clean(instructions, s.Vocabulary, s.OutputLanguage, sample);
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
