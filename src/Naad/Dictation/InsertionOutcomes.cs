using System.Diagnostics;
using Naad.Insertion;

namespace Naad.Dictation;

/// <summary>What a transcript that wasn't inserted is kept as, and the log event saying why.</summary>
/// <param name="Reason">What the user is told.</param>
/// <param name="Event">What is logged.</param>
internal readonly record struct KeptFor(TranscriptKeptReason Reason, LogEvent Event);

/// <summary>Turns the focus guard's decisions and the inserter's outcomes into what to keep and log. Pure.</summary>
internal static class InsertionOutcomes
{
    /// <summary>Why a transcript wasn't inserted because of where focus was.</summary>
    public static KeptFor ForDecision(InsertionDecision decision) => decision switch
    {
        InsertionDecision.TargetElevated => new(TranscriptKeptReason.TargetElevated, LogEvent.PasteSkippedTargetElevated),
        InsertionDecision.TargetClosed => new(TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedTargetClosed),
        InsertionDecision.NoTarget => new(TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedNoTarget),
        _ => new(TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedFocusChanged),
    };

    /// <summary>
    /// For an outcome where the paste failed, what to keep; null when the text went in or nothing
    /// needs keeping. TargetChanged is decided by <see cref="ForDecision"/> from the focus now.
    /// </summary>
    public static KeptFor? ForFailure(InsertionOutcome outcome) => outcome switch
    {
        InsertionOutcome.Pasted or InsertionOutcome.PastedClipboardChanged or InsertionOutcome.PastedRestoreFailed
            or InsertionOutcome.Typed or InsertionOutcome.Cancelled or InsertionOutcome.TargetChanged => null,
        InsertionOutcome.ModifiersHeld => new(TranscriptKeptReason.NotInserted, LogEvent.PasteSkippedModifiersHeld),
        InsertionOutcome.PasteRejected => new(TranscriptKeptReason.NotInserted, LogEvent.PasteRejected),
        InsertionOutcome.ClipboardUnavailable => new(TranscriptKeptReason.NotInserted, LogEvent.ClipboardUnavailable),
        InsertionOutcome.TypedPartially => new(TranscriptKeptReason.NotInserted, LogEvent.TypingIncomplete),

        // The session allows one insertion at a time, so the inserter is never busy here.
        _ => throw new UnreachableException($"Unexpected insertion outcome {outcome}."),
    };
}
