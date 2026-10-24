namespace Naad;

/// <summary>
/// Everything Naad can log. Events are a closed set so no call site can pass free text
/// (a transcript, clipboard contents, a window title) to the log.
/// </summary>
internal enum LogEvent
{
    AppStarted,
    AppStopped,
    SecondInstanceRejected,
    ApiKeyMissing,
    StartupFailed,
    UnhandledUiException,
    UnhandledException,
    UnobservedTaskException,

    RecordingStarted,
    MicrophoneUnavailable,
    DictationCancelledByKey,
    DictationIgnoredShortHold,
    RecordingStopped,
    TranscriptionCompleted,
    TranscriptionEmpty,
    TranscriptionFailed,
    TranscriptionTimedOut,
    DictationCancelledByShutdown,
    DictationFailed,

    PasteSkippedFocusChanged,
    PasteSkippedModifiersHeld,
    PasteRejected,
    Pasted,
}
