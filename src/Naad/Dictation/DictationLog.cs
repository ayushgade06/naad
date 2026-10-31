using Naad.Audio;
using Naad.Insertion;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>
/// Turns dictation outcomes into fixed log events with numbers only, so the controller stays
/// about what happens rather than how it is logged.
/// </summary>
internal static class DictationLog
{
    public static void Microphone(MicrophoneFailure failure)
    {
        switch (failure.Error)
        {
            case MicrophoneError.NotFound:
                Log.Warn(LogEvent.MicrophoneNotFound, ("mm_result", failure.Code));
                break;
            case MicrophoneError.Blocked:
                Log.Warn(LogEvent.MicrophoneBlocked);
                break;
            case MicrophoneError.InUse:
                Log.Warn(LogEvent.MicrophoneInUse, ("mm_result", failure.Code));
                break;
            default:
                Log.Warn(LogEvent.MicrophoneUnavailable, ("mm_result", failure.Code));
                break;
        }
    }

    public static void Discarded(DictationEvent cause, TimeSpan held)
    {
        switch (cause)
        {
            case DictationEvent.Released:
                Log.Info(LogEvent.DictationIgnoredShortHold, ("held_ms", Milliseconds(held)));
                break;
            case DictationEvent.CancelledByMouse:
                Log.Info(LogEvent.DictationCancelledByMouse);
                break;
            default:
                Log.Info(LogEvent.DictationCancelledByKey);
                break;
        }
    }

    public static void Rejected(RecordingCheck check) => Log.Info(
        check.Verdict == RecordingVerdict.TooShort ? LogEvent.RecordingTooShort : LogEvent.RecordingSilent,
        ("audio_ms", Milliseconds(check.Duration)),
        ("peak", check.Peak),
        ("rms", check.Rms));

    public static void Transcription(TranscriptionFailure failure)
    {
        switch (failure.Error)
        {
            case TranscriptionError.Timeout:
                Log.Warn(LogEvent.TranscriptionTimedOut);
                break;
            case TranscriptionError.MalformedResponse:
                Log.Warn(LogEvent.TranscriptionMalformedResponse);
                break;
            case TranscriptionError.Network or TranscriptionError.NoInternet or TranscriptionError.ConnectionLost:
                Log.Warn(
                    LogEvent.TranscriptionFailed, ("error", (int)failure.Error), ("network_error", failure.Code));
                break;
            default:
                Log.Warn(LogEvent.TranscriptionFailed, ("http_status", failure.Code));
                break;
        }
    }

    /// <summary>Logs how text that went in got there; failed outcomes are logged when the text is kept.</summary>
    public static void Inserted(InsertionOutcome outcome, int chars)
    {
        switch (outcome)
        {
            case InsertionOutcome.PastedClipboardChanged:
                Log.Info(LogEvent.ClipboardRestoreSkippedChanged);
                break;
            case InsertionOutcome.PastedRestoreFailed:
                Log.Warn(LogEvent.ClipboardRestoreFailed);
                break;
            case InsertionOutcome.Typed:
                Log.Info(LogEvent.InsertedByTyping, ("chars", chars));
                break;
            case InsertionOutcome.Cancelled:
                Log.Info(LogEvent.DictationCancelledByShutdown);
                break;
        }
    }

    public static long Milliseconds(TimeSpan duration) => (long)duration.TotalMilliseconds;
}
