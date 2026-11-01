using Naad.Context;

namespace Naad.Cleanup;

internal enum CleanupOutcome
{
    /// <summary>Cleanup is off: the transcript as it was said.</summary>
    Unchanged,

    /// <summary>The rule-based cleanup ran.</summary>
    Basic,

    /// <summary>The language model cleaned or translated it.</summary>
    Cleaned,

    /// <summary>The model found nothing worth pasting.</summary>
    Empty,

    /// <summary>The model failed, was too slow or gave an unusable reply: the transcript as it was said.</summary>
    FellBack,

    /// <summary>The selected text was rewritten as the dictation asked.</summary>
    Edited,

    /// <summary>The rewrite failed: nothing is pasted, so the selection stays as it was.</summary>
    EditFailed,
}

/// <param name="Text">What to paste.</param>
/// <param name="Outcome">How it was made.</param>
/// <param name="PressEnter">Whether Enter is pressed after the paste ("press enter" was said).</param>
internal readonly record struct CleanupResult(string Text, CleanupOutcome Outcome, bool PressEnter = false)
{
    // The generated ToString would print the text.
    public override string ToString() => $"CleanupResult {{ Chars = {Text.Length}, Outcome = {Outcome} }}";
}

/// <summary>
/// Turns a transcript into the text to paste. Never fails: the worst case is the transcript
/// itself, or for an edit, nothing at all.
/// </summary>
internal interface ITranscriptCleanup
{
    /// <summary>Whether a language model runs, so the user is told it is cleaning up.</summary>
    bool UsesModel { get; }

    /// <summary>A dictation started: a model on this PC may start loading while the user speaks.</summary>
    void Prepare();

    /// <param name="transcript">What was said.</param>
    /// <param name="context">What was read from the app; its selection makes this an edit.</param>
    /// <param name="cancellationToken">Cancelled at shutdown or by Esc.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CleanupResult> CleanAsync(string transcript, DictationContext context, CancellationToken cancellationToken);
}
