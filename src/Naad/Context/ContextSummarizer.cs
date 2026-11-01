using Naad.LanguageModel;

namespace Naad.Context;

/// <param name="Text">Two sentences on what the user is doing; user content, never logged. Null for none.</param>
/// <param name="Failure">Why there is none, when the context model failed; null otherwise.</param>
internal sealed record ContextSummary(string? Text, ContextFailure? Failure = null)
{
    public static ContextSummary None { get; } = new(Text: null);

    // The generated ToString would print the text.
    public override string ToString() => $"ContextSummary {{ Chars = {Text?.Length ?? 0}, Failure = {Failure} }}";
}

/// <summary>
/// Asks the context model what the user is doing, from what was read of the app, while they speak:
/// it starts with the recording, so by the time cleanup needs it, it is usually ready.
/// </summary>
/// <param name="model">The context model.</param>
/// <param name="customInstructions">The user's own context prompt, or null for the default; read each time.</param>
/// <param name="timeout">How long a summary may take; the recording is usually longer.</param>
internal sealed class ContextSummarizer(ILanguageModel model, Func<string?> customInstructions, TimeSpan timeout)
{
    /// <summary>
    /// A summary of <paramref name="context"/>, once it is read; None when there is nothing to go on.
    /// </summary>
    public async Task<ContextSummary> SummarizeAsync(Task<DictationContext> context, CancellationToken token)
    {
        DictationContext read;
        try
        {
            read = await context;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The dictation reads it again and goes on without it.
            Log.Error(LogEvent.ContextReadFailed, e);
            return ContextSummary.None;
        }

        // An app that told nothing gives nothing to summarise.
        if (read is { App: null, WindowTitle: null, NearbyText: null, Screenshot: null })
        {
            return ContextSummary.None;
        }

        return await AskAsync(ContextPrompt.Build(customInstructions(), read), token);
    }

    /// <summary>Sends <paramref name="prompt"/> as it is, as Test context prompt does.</summary>
    public async Task<ContextSummary> AskAsync(ChatPrompt prompt, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        CompletionResult reply;
        try
        {
            reply = await model.CompleteAsync(prompt, limit.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            reply = new("", new(CompletionError.Timeout));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A model on this PC disposed by a settings change, say: the dictation goes on without it.
            Log.Error(LogEvent.ContextSummaryFailed, e);
            return new(null, ContextFailure.Unusable);
        }

        var failure = reply.Failure is { } f ? ContextFailures.From(f)
            : ContextPrompt.TwoSentences(reply.Text) is null ? ContextFailure.Unusable
            : (ContextFailure?)null;
        if (failure is { } why)
        {
            Log.Warn(LogEvent.ContextSummaryFailed, ("error", (int)why));
            return new(null, why);
        }

        Log.Info(LogEvent.ContextSummarized, ("chars", reply.Text.Length));
        return new(ContextPrompt.TwoSentences(reply.Text));
    }
}
