namespace Naad.Dictation;

/// <summary>Why a transcript was kept for the user instead of being inserted.</summary>
internal enum TranscriptKeptReason
{
    /// <summary>The user switched windows, or closed the target, before the text was ready.</summary>
    WindowChanged,

    /// <summary>The target runs as administrator, and Windows blocks Naad's input to it.</summary>
    TargetElevated,

    /// <summary>Pasting and typing both failed, or a modifier key was still held.</summary>
    NotInserted,

    /// <summary>Live typing stopped part way: focus moved, or the words were revised.</summary>
    LiveTypingStopped,
}
