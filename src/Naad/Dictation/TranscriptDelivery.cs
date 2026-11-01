using Naad.Insertion;

namespace Naad.Dictation;

/// <summary>
/// Puts finished text into the window it was meant for, or keeps it to copy when it can't go
/// in. Text goes only into the window the user dictated into, and only if it can take input;
/// focus is never switched. Use on the UI thread. Logs events and counts, never the text.
/// </summary>
/// <param name="inserter">Pastes or types.</param>
/// <param name="checkTarget">Whether a window may receive text now.</param>
/// <param name="pressEnter">Presses Enter in the foreground window; false if Windows refused.</param>
/// <param name="time">Measures release-to-paste time.</param>
internal sealed class TranscriptDelivery(
    TextInserter inserter,
    Func<WindowTarget, InsertionDecision> checkTarget,
    Func<bool> pressEnter,
    TimeProvider time)
{
    // The last text that couldn't be inserted, for the user to copy. Memory only, one at a time.
    private string? _kept;

    /// <summary>A text wasn't inserted and is kept for <see cref="CopyKeptAsync"/>. Raised on the UI thread.</summary>
    public event Action<TranscriptKeptReason>? Kept;

    /// <summary>Presses Enter, if <paramref name="target"/> still has the focus.</summary>
    public void PressEnter(WindowTarget target)
    {
        if (checkTarget(target) == InsertionDecision.Insert)
        {
            pressEnter();
        }
    }

    /// <summary>
    /// Pastes <paramref name="text"/> into <paramref name="target"/>, then presses Enter if asked.
    /// Returns whether the text went in.
    /// </summary>
    public async Task<bool> DeliverAsync(
        string text, WindowTarget target, long releasedAt, bool thenEnter, CancellationToken shutdown)
    {
        var decision = checkTarget(target);
        if (decision != InsertionDecision.Insert)
        {
            Keep(text, InsertionOutcomes.ForDecision(decision));
            return false;
        }

        var outcome = await inserter.PasteAsync(
            text,
            canInsert: () => checkTarget(target) == InsertionDecision.Insert,
            pasted: () => Log.Info(
                LogEvent.Pasted,
                ("release_to_text_ms", DictationLog.Milliseconds(time.GetElapsedTime(releasedAt)))),
            shutdown);
        if (inserter.LastSkippedFormats > 0 && outcome is InsertionOutcome.Pasted)
        {
            Log.Warn(LogEvent.ClipboardFormatsSkipped, ("formats", inserter.LastSkippedFormats));
        }

        DictationLog.Inserted(outcome, text.Length);
        switch (outcome)
        {
            case InsertionOutcome.Pasted or InsertionOutcome.PastedClipboardChanged
                or InsertionOutcome.PastedRestoreFailed or InsertionOutcome.Typed:
                // Only into the same window: a focus change since would send Enter elsewhere.
                if (thenEnter && checkTarget(target) == InsertionDecision.Insert)
                {
                    pressEnter();
                }

                return true;
            case InsertionOutcome.TargetChanged:
                // Focus moved while waiting for the modifiers; say why as precisely as possible.
                var now = checkTarget(target);
                Keep(text, InsertionOutcomes.ForDecision(now == InsertionDecision.Insert
                    ? InsertionDecision.FocusChanged
                    : now));
                return false;
            default:
                if (InsertionOutcomes.ForFailure(outcome) is { } kept)
                {
                    Keep(text, kept);
                }

                return false;
        }
    }

    /// <summary>
    /// Puts the kept text on the clipboard, out of clipboard history, for the user to paste. Returns
    /// false if there is none or the clipboard is busy.
    /// </summary>
    public Task<bool> CopyKeptAsync() => _kept is { } text ? CopyAsync(text) : Task.FromResult(false);

    /// <summary>Puts <paramref name="text"/> on the clipboard; false if the clipboard is busy.</summary>
    public async Task<bool> CopyAsync(string text)
    {
        if (!await inserter.CopyAsync(text))
        {
            Log.Warn(LogEvent.TranscriptCopyFailed);
            return false;
        }

        Log.Info(LogEvent.TranscriptCopied, ("chars", text.Length));
        return true;
    }

    private void Keep(string text, KeptFor kept)
    {
        _kept = text;
        Log.Warn(kept.Event);
        Kept?.Invoke(kept.Reason);
    }
}
