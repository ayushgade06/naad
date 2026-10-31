using System.Diagnostics;
using Naad.LanguageModel;
using Naad.Settings;

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
    /// The language model the settings choose, or null when it can't run yet. <paramref name="key"/>
    /// is the language model key, or null.
    /// </summary>
    public static ILanguageModel? CreateModel(
        HttpClient http, string? key, AppSettings s, TemperatureSupport temperature) =>
        LanguageModelSelection.Choose(
            s.LanguageModelProvider,
            key is not null,
            s.LanguageModelBaseUrl.IsLoopback,
            LanguageModelFiles.For(s.LocalLanguageModel).IsReady()) switch
        {
            LanguageModelProvider.Cloud => new OpenAiChatLanguageModel(
                http, key, s.LanguageModelBaseUrl, s.LanguageModelName, temperature),
            LanguageModelProvider.Local => new LocalLanguageModel(
                LanguageModelFiles.For(s.LocalLanguageModel).Path, TimeProvider.System),
            _ => null,
        };

    /// <summary>
    /// The cleanup step for <paramref name="s"/>, using <paramref name="model"/> and, in the
    /// cloud, the fallback model when one is set.
    /// </summary>
    public static TranscriptCleanup Create(
        ILanguageModel? model,
        HttpClient http,
        string? key,
        AppSettings s,
        TemperatureSupport temperature,
        RateLimitCooldowns cooldowns)
    {
        if (model is null)
        {
            return new(s.Cleanup, ai: null);
        }

        // Rate limits are per cloud model; a model on this PC has none.
        var models = new List<NamedModel> { new(model is LocalLanguageModel ? "local" : s.LanguageModelName, model) };
        if (model is OpenAiChatLanguageModel && s.LanguageModelFallbackName is { } fallback
            && fallback != s.LanguageModelName)
        {
            models.Add(new(fallback, new OpenAiChatLanguageModel(
                http, key, s.LanguageModelBaseUrl, fallback, temperature)));
        }

        var options = new CleanupOptions(s.CleanupPrompt, s.Vocabulary, s.OutputLanguage, s.InstructionGuard);
        return new(s.Cleanup, new AiCleanup(models, cooldowns, options));
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
