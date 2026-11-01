namespace Naad.History;

/// <summary>How a dictation ended.</summary>
internal enum HistoryOutcome
{
    /// <summary>The text went into the app.</summary>
    Pasted,

    /// <summary>The text couldn't go in (focus moved, for example) and was kept to copy.</summary>
    Kept,

    /// <summary>Transcription failed; the audio, if kept, can be tried again.</summary>
    Failed,
}

/// <summary>
/// One dictation in the history. User content: stored encrypted, shown in Naad's window, never
/// logged.
/// </summary>
/// <param name="Id">Names its files.</param>
/// <param name="Time">When the dictation ended.</param>
/// <param name="App">The process name of the app dictated into, such as "notepad", or null.</param>
/// <param name="AudioMilliseconds">How long the recording was.</param>
/// <param name="Raw">What the transcriber heard; empty when it failed.</param>
/// <param name="Text">What was pasted or kept: the cleaned text, or the raw one.</param>
/// <param name="TranscribeMilliseconds">How long transcription took.</param>
/// <param name="CleanupMilliseconds">How long cleanup took; 0 when none ran.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="HasAudio">Whether the recording is kept with it.</param>
/// <param name="Selection">For an Edit Mode dictation, the text it rewrote; null for a dictation.</param>
/// <param name="Invocation">How that edit was asked for: Automatic or Manual. Null for a dictation, and for
/// edits recorded before Naad kept it.</param>
internal sealed record HistoryRecord(
    Guid Id,
    DateTimeOffset Time,
    string? App,
    long AudioMilliseconds,
    string Raw,
    string Text,
    long TranscribeMilliseconds,
    long CleanupMilliseconds,
    HistoryOutcome Outcome,
    bool HasAudio,
    string? Selection = null,
    Naad.Context.EditInvocation? Invocation = null)
{
    // The generated ToString would print what was said.
    public override string ToString() => $"HistoryRecord {{ Id = {Id}, Outcome = {Outcome}, Chars = {Text.Length} }}";
}
