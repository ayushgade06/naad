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
}

/// <param name="Text">What to paste.</param>
/// <param name="Outcome">How it was made.</param>
internal readonly record struct CleanupResult(string Text, CleanupOutcome Outcome)
{
    // The generated ToString would print the text.
    public override string ToString() => $"CleanupResult {{ Chars = {Text.Length}, Outcome = {Outcome} }}";
}

/// <summary>Turns a transcript into the text to paste. Never fails: the worst case is the transcript itself.</summary>
internal interface ITranscriptCleanup
{
    /// <summary>A dictation started: a model on this PC may start loading while the user speaks.</summary>
    void Prepare();

    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CleanupResult> CleanAsync(string transcript, CancellationToken cancellationToken);
}
