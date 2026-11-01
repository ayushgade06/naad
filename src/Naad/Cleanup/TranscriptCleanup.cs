using System.Diagnostics;
using Naad.Context;
using Naad.Snippets;

namespace Naad.Cleanup;

/// <summary>How much a transcript is tidied before it is pasted.</summary>
internal enum CleanupLevel
{
    /// <summary>Pasted as it was said.</summary>
    Off,

    /// <summary>Rule-based and instant (see <see cref="BasicCleanup"/>).</summary>
    Basic,

    /// <summary>By the language model, with the vocabulary in its prompt.</summary>
    Ai,
}

/// <summary>
/// Applies the chosen <see cref="CleanupLevel"/> within the budget, then the snippets and the
/// "press enter" command. Without a language model, Ai cleans like Basic. A language model that
/// fails, runs over the budget or gives an unusable reply leaves the transcript as it was said, so
/// a dictation is never lost to AI. An edit (a dictation over selected text) that fails gives
/// nothing, so the selection is left as it was.
/// </summary>
/// <param name="level">The chosen level.</param>
/// <param name="ai">The language model step, or null when none is set up.</param>
/// <param name="budget">How long the model may take; the app passes its setting.</param>
/// <param name="snippets">Spoken triggers to expand, after cleanup.</param>
/// <param name="pressEnterCommand">Whether a trailing "press enter" presses Enter after the paste.</param>
internal sealed class TranscriptCleanup(
    CleanupLevel level,
    AiCleanup? ai,
    TimeSpan? budget = null,
    IReadOnlyList<Snippet>? snippets = null,
    bool pressEnterCommand = false) : ITranscriptCleanup
{
    /// <summary>
    /// The longest a dictation waits for the language model by default: about twice the large
    /// model's time on a laptop processor for a long sentence, yet short enough not to feel stuck.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private static int _fallbacks;

    public bool UsesModel => ai is not null && (level == CleanupLevel.Ai || ai.Translates);

    public void Prepare() => ai?.Prepare();

    public async Task<CleanupResult> CleanAsync(
        string transcript, DictationContext context, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var (result, reason) = context.IsEdit
            ? await EditAsync(context.Selection!, transcript, cancellationToken)
            : await RunAsync(transcript, context, cancellationToken);
        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.Outcome is CleanupOutcome.FellBack or CleanupOutcome.EditFailed)
        {
            // The count since Naad started shows how often AI is failing the user.
            Log.Warn(
                result.Outcome == CleanupOutcome.EditFailed ? LogEvent.EditFailed : LogEvent.CleanupFellBack,
                ("reason", (int)reason),
                ("cleanup_ms", milliseconds),
                ("count", Interlocked.Increment(ref _fallbacks)));
        }
        else
        {
            Log.Info(
                LogEvent.CleanupCompleted,
                ("level", (int)level),
                ("outcome", (int)result.Outcome),
                ("cleanup_ms", milliseconds),
                ("chars_in", transcript.Length),
                ("chars_out", result.Text.Length));
        }

        if (result.Outcome is CleanupOutcome.EditFailed or CleanupOutcome.Edited or CleanupOutcome.Empty
            || result.Text.Length == 0)
        {
            return result;
        }

        var text = snippets is { Count: > 0 } ? TranscriptCommands.Expand(result.Text, snippets) : result.Text;
        var (final, enter) = pressEnterCommand ? TranscriptCommands.PressEnter(text) : (text, false);
        return result with { Text = final, PressEnter = enter };
    }

    private async Task<(CleanupResult, FallbackReason)> EditAsync(
        string selection, string instruction, CancellationToken cancellationToken)
    {
        if (ai is null)
        {
            return (new("", CleanupOutcome.EditFailed), FallbackReason.Failed);
        }

        return await WithinBudgetAsync(
            token => ai.EditAsync(selection, instruction, token),
            new("", CleanupOutcome.EditFailed),
            cancellationToken);
    }

    private async Task<(CleanupResult, FallbackReason)> RunAsync(
        string transcript, DictationContext context, CancellationToken cancellationToken)
    {
        var text = level == CleanupLevel.Off ? transcript : BasicCleanup.Clean(transcript);
        var outcome = level == CleanupLevel.Off ? CleanupOutcome.Unchanged : CleanupOutcome.Basic;
        if (!UsesModel)
        {
            return (new(text, outcome), FallbackReason.None);
        }

        return await WithinBudgetAsync(
            token => level == CleanupLevel.Ai
                ? ai!.CleanAsync(transcript, context, token)
                : ai!.TranslateAsync(text, token),
            new(transcript, CleanupOutcome.FellBack),
            cancellationToken);
    }

    // WaitAsync holds the budget even when the model can't stop at once, as while it loads.
    private async Task<(CleanupResult, FallbackReason)> WithinBudgetAsync(
        Func<CancellationToken, Task<(CleanupResult, FallbackReason)>> run,
        CleanupResult onTimeout,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget ?? Budget);
        try
        {
            return await run(timeout.Token).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (onTimeout, FallbackReason.Timeout);
        }
    }
}
