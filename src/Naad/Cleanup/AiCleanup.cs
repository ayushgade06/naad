using Naad.Context;
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
/// <param name="ContextModel">A cloud model that describes a screenshot of the app, or null.</param>
/// <param name="SpokenLanguage">The language set for dictation, such as "de", or null when detected.</param>
internal sealed record CleanupOptions(
    string? Instructions,
    string? Vocabulary,
    string? OutputLanguage,
    bool Guard,
    ILanguageModel? ContextModel = null,
    string? SpokenLanguage = null)
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
        else if (models[0].Model is OfflineFallbackLanguageModel fallback)
        {
            fallback.Prepare();
        }
    }

    /// <summary>
    /// Cleans <paramref name="transcript"/>. The model gets it already through the instant rules,
    /// which small models then only need to polish; nothing but hesitations never reaches it, as
    /// a small model may invent text for an empty request.
    /// </summary>
    public async Task<(CleanupResult Result, FallbackReason Reason)> CleanAsync(
        string transcript, DictationContext context, CancellationToken token)
    {
        var basic = BasicCleanup.Clean(transcript);
        if (basic.Length == 0)
        {
            return (new("", CleanupOutcome.Empty), FallbackReason.None);
        }

        var summary = await DescribeAsync(context, token);
        return await RunAsync(
            CleanupPrompt.Clean(
                options.Instructions,
                options.Vocabulary,
                options.OutputLanguage,
                basic,
                context,
                summary,
                options.SpokenLanguage),
            transcript,
            token);
    }

    /// <summary>
    /// Rewrites <paramref name="selection"/> as <paramref name="instruction"/> asks. A failure or
    /// an unusable reply comes back as EditFailed, so the selection is left alone.
    /// </summary>
    public async Task<(CleanupResult Result, FallbackReason Reason)> EditAsync(
        string selection, string instruction, CancellationToken token)
    {
        var (result, reason) = await RunAsync(
            CleanupPrompt.Edit(selection, instruction, options.Vocabulary), selection, token, editing: true);
        return result.Outcome == CleanupOutcome.Cleaned
            ? (result with { Outcome = CleanupOutcome.Edited }, reason)
            : (new("", CleanupOutcome.EditFailed), reason == FallbackReason.None ? FallbackReason.Rejected : reason);
    }

    // A cloud context model's two-sentence account of a screenshot, or null. Its failure leaves the
    // prompt without it; it never costs the dictation.
    // Cleanup without the summary beats a cleanup that runs out of time.
    private static readonly TimeSpan DescribeBudget = TimeSpan.FromSeconds(3);

    private async Task<string?> DescribeAsync(DictationContext context, CancellationToken token)
    {
        if (context.Screenshot is not { } image || options.ContextModel is not { } model)
        {
            return null;
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(DescribeBudget);
        CompletionResult reply;
        try
        {
            reply = await model.CompleteAsync(CleanupPrompt.DescribeScreenshot(image), wait.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Log.Warn(LogEvent.ContextSummaryFailed, ("error", -2));
            return null;
        }

        if (reply.Failure is not null || reply.Text.Length == 0)
        {
            Log.Warn(LogEvent.ContextSummaryFailed, ("error", reply.Failure is { } f ? (int)f.Error : -1));
            return null;
        }

        return reply.Text;
    }

    /// <summary>Translates without tidying, for when cleanup is off. Needs <see cref="Translates"/>.</summary>
    public Task<(CleanupResult Result, FallbackReason Reason)> TranslateAsync(
        string transcript, CancellationToken token) =>
        RunAsync(CleanupPrompt.Translate(options.OutputLanguage!, transcript), transcript, token);

    private async Task<(CleanupResult, FallbackReason)> RunAsync(
        ChatPrompt prompt, string transcript, CancellationToken token, bool editing = false)
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
                Log.Warn(LogEvent.CleanupModelFailed, ("error", (int)failure.Error), ("code", failure.Code));
                if (failure.Error != CompletionError.RateLimited)
                {
                    return (Raw(transcript), FallbackReason.Failed);
                }

                cooldowns.Start(name, failure.Wait ?? RateLimitHeaders.DefaultWait, failure.Daily);
                reason = FallbackReason.Failed;
                continue;
            }

            // An edit may rewrite every word and change the length, so only chat answers are refused.
            var check = CleanupReplyCheck.Check(
                transcript, result.Text, translating: Translates || editing, guard: options.Guard && !editing);
            switch (check.Verdict)
            {
                case ReplyVerdict.Accepted:
                    // Small models sometimes leave the first letter small or the last stop off; a
                    // translation is left as the model wrote it.
                    var text = Translates || editing ? check.Text : BasicCleanup.Finish(check.Text);
                    return (new(text, CleanupOutcome.Cleaned), FallbackReason.None);
                case ReplyVerdict.Empty when !editing:
                    return (new("", CleanupOutcome.Empty), FallbackReason.None);
                default:
                    Log.Warn(LogEvent.CleanupReplyRefused, ("verdict", (int)check.Verdict));
                    reason = FallbackReason.Rejected;
                    break;
            }
        }

        return (Raw(transcript), reason);
    }

    private static CleanupResult Raw(string transcript) => new(transcript, CleanupOutcome.FellBack);
}
