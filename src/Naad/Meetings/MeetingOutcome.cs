namespace Naad.Meetings;

/// <summary>How starting or stopping a meeting went, for the tray to say.</summary>
internal enum MeetingOutcome
{
    Started,

    /// <summary>Nothing changed: a start or stop was already under way.</summary>
    Unchanged,

    /// <summary>A dictation is running; meetings and dictation never record at once.</summary>
    DictationRunning,

    /// <summary>No transcriber is set up yet.</summary>
    NoTranscriber,

    /// <summary>The microphone or the PC's sound couldn't be opened.</summary>
    CaptureFailed,

    /// <summary>Stopped; the transcript is saved and on the clipboard.</summary>
    Copied,

    /// <summary>Stopped; on the clipboard, but saving failed.</summary>
    CopiedNotSaved,

    /// <summary>Stopped; neither saved nor copied.</summary>
    NotSaved,

    /// <summary>Stopped; saved, but the clipboard was busy.</summary>
    SavedNotCopied,

    /// <summary>Stopped with no speech heard: nothing was saved.</summary>
    NothingHeard,
}
