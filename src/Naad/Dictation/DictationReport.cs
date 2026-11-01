using Naad.History;

namespace Naad.Dictation;

/// <summary>
/// What a finished dictation produced, for the history. User content: the history keeps it
/// encrypted, and it is never logged.
/// </summary>
/// <param name="ProcessId">The app dictated into, to name it.</param>
/// <param name="AudioMilliseconds">How long the recording was.</param>
/// <param name="TranscribeMilliseconds">How long transcription took.</param>
/// <param name="CleanupMilliseconds">How long cleanup (or the edit) took.</param>
/// <param name="Raw">What was heard.</param>
/// <param name="Text">What was pasted or kept.</param>
/// <param name="Selection">The text an edit rewrote, or null for a dictation.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Wav">The recording.</param>
/// <param name="Invocation">How an edit was asked for; null for a dictation.</param>
/// <param name="LiveFellBack">Live transcription failed, so the recording was transcribed in one pass.</param>
internal sealed record DictationReport(
    uint ProcessId,
    long AudioMilliseconds = 0,
    long TranscribeMilliseconds = 0,
    long CleanupMilliseconds = 0,
    string Raw = "",
    string Text = "",
    string? Selection = null,
    HistoryOutcome Outcome = HistoryOutcome.Failed,
    byte[]? Wav = null,
    Naad.Context.EditInvocation? Invocation = null,
    bool LiveFellBack = false)
{
    // The generated ToString would print what was said.
    public override string ToString() => $"DictationReport {{ Outcome = {Outcome}, Chars = {Text.Length} }}";
}
