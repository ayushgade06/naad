using Naad.LanguageModel;

namespace Naad.Cleanup;

/// <summary>Why the language model's text wasn't used. Numbers are logged.</summary>
internal enum FallbackReason
{
    None = 0,
    Timeout = 1,
    Failed = 2,
    Rejected = 3,
    CoolingDown = 4,
}

/// <param name="Instructions">The user's own cleanup instructions, or null for the default.</param>
/// <param name="Vocabulary">Names and terms to spell exactly, or null.</param>
/// <param name="OutputLanguage">A language to write the result in, or null to keep the speaker's.</param>
/// <param name="Guard">Whether to refuse replies that answer the dictation instead of tidying it.</param>
internal sealed record CleanupOptions(string? Instructions, string? Vocabulary, string? OutputLanguage, bool Guard)
{
    // The generated ToString would print the instructions and vocabulary.
    public override string ToString() => $"CleanupOptions {{ Guard = {Guard} }}";
}

/// <summary>A language model and the name its rate limits are kept under.</summary>
internal sealed record NamedModel(string Name, ILanguageModel Model);

/// <summary>
/// Cleans or translates a transcript with a language model. A rate limit, a blank reply or one
/// that answers the dictation moves on to the fallback model when there is one; any other
/// failure, or running out of models, gives the transcript back unchanged. Never logs text.
/// </summary>
/// <param name="models">The model to use, then the fallback, if any.</param>
/// <param name="cooldowns">Models whose rate limit is still running are skipped.</param>
/// <param name="options">What the prompt says.</param>
internal sealed class AiCleanup(IReadOnlyList<NamedModel> models, RateLimitCooldowns cooldowns, CleanupOptions options)
{
    /// <summary>Whether text is written in another language than the speaker's.</summary>
    public bool Translates => options.OutputLanguage is not null;

    /// <summary>Starts loading a model on this PC, if that is what runs first.</summary>
    public void Prepare()
    {
        if (models[0].Model is LocalLanguageModel local)
        {
            _ = local.PreloadAsync();
        }
    }

    public Task<(CleanupResult Result, FallbackReason Reason)> CleanAsync(string transcript, CancellationToken token) =>
        RunAsync(
            CleanupPrompt.Clean(options.Instructions, options.Vocabulary, options.OutputLanguage, transcript),
            transcript,
            token);

    /// <summary>Translates without tidying, for when cleanup is off. Needs <see cref="Translates"/>.</summary>
    public Task<(CleanupResult Result, FallbackReason Reason)> TranslateAsync(
        string transcript, CancellationToken token) =>
        RunAsync(CleanupPrompt.Translate(options.OutputLanguage!, transcript), transcript, token);

    private async Task<(CleanupResult, FallbackReason)> RunAsync(
        ChatPrompt prompt, string transcript, CancellationToken token)
    {
        var reason = FallbackReason.CoolingDown;
        for (var i = 0; i < models.Count; i++)
        {
            var (name, model) = models[i];
            if (cooldowns.For(name) is not null)
            {
                Log.Info(LogEvent.CleanupSkippedCoolingDown, ("model_index", i));
                continue;
            }

            if (i > 0)
            {
                Log.Info(LogEvent.CleanupFallbackModelUsed);
            }

            var result = await model.CompleteAsync(prompt, token);
            if (result.Failure is { } failure)
            {
                Log.Warn(LogEvent.CleanupFellBack, ("error", (int)failure.Error), ("code", failure.Code));
                if (failure.Error != CompletionError.RateLimited)
                {
                    return (Raw(transcript), FallbackReason.Failed);
                }

                cooldowns.Start(name, failure.Wait ?? RateLimitHeaders.DefaultWait, failure.Daily);
                reason = FallbackReason.Failed;
                continue;
            }

            var check = CleanupReplyCheck.Check(transcript, result.Text, Translates, options.Guard);
            switch (check.Verdict)
            {
                case ReplyVerdict.Accepted:
                    return (new(check.Text, CleanupOutcome.Cleaned), FallbackReason.None);
                case ReplyVerdict.Empty:
                    return (new("", CleanupOutcome.Empty), FallbackReason.None);
                default:
                    Log.Warn(LogEvent.CleanupFellBack, ("verdict", (int)check.Verdict));
                    reason = FallbackReason.Rejected;
                    break;
            }
        }

        return (Raw(transcript), reason);
    }

    private static CleanupResult Raw(string transcript) => new(transcript, CleanupOutcome.FellBack);
}
