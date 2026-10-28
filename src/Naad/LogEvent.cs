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
    ApiKeyUnreadable,
    ApiKeyFromEnvironment,
    ApiKeyLoaded,
    ApiKeyStored,
    ApiKeyStoreFailed,
    FatalError,
    UnhandledUiException,
    UnhandledException,
    UnobservedTaskException,

    SettingsLoaded,
    SettingsCreated,
    SettingsMigrated,
    SettingsRepaired,
    SettingsCorrupt,
    SettingsFutureVersion,
    SettingsReadFailed,
    SettingsWriteFailed,

    RecordingStarted,
    MicrophoneNotFound,
    MicrophoneBlocked,
    MicrophoneInUse,
    MicrophoneUnavailable,
    RecordingStopTimedOut,
    DictationCancelledByKey,
    DictationCancelledByMouse,
    MouseHookUnavailable,
    DictationIgnoredShortHold,
    RecordingStopped,
    RecordingTooShort,
    RecordingSilent,
    RecordingMaxDurationReached,
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
