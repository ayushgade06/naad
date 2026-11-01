using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Transcription;

namespace Naad.Tray;

/// <summary>The words the tray shows: tooltips and notification texts. Pure.</summary>
internal sealed partial class TrayIcon
{
    /// <summary>
    /// Also the icon's accessible name, which UI tests use to find it. NotifyIcon refuses
    /// text over 127 characters; a chord's four-key limit keeps it well under that.
    /// </summary>
    public static string Tooltip(Chord shortcut, bool handsFree = false) => handsFree
        ? $"Naad: press {shortcut.Describe()} to start and stop dictating"
        : $"Naad: hold {shortcut.Describe()} to dictate";

    /// <summary>The tooltip while listening: how to end the dictation.</summary>
    public static string RecordingTooltip(Chord shortcut, bool handsFree) => handsFree
        ? $"Naad: listening. Press {shortcut.Describe()} again to transcribe."
        : "Naad: listening. Release to transcribe.";

    /// <summary>Asks before updating. Windows cuts notification text at 255 characters.</summary>
    public static string UpdateAvailableMessage(string version) =>
        $"Naad {version} is available. Click here to download and install it; Naad restarts by itself.";

    /// <summary>What to tell the user. Windows cuts notification text at 255 characters.</summary>
    public static string MicrophoneErrorMessage(MicrophoneError error) => error switch
    {
        MicrophoneError.Blocked => "Windows privacy settings block the microphone. Click here, then turn on " +
            "Microphone access and Let desktop apps access your microphone.",
        MicrophoneError.NotFound => "No microphone was found. Connect one, or pick an input device in " +
            "Settings > System > Sound.",
        MicrophoneError.InUse => "Another app is using the microphone. Close it, then try again.",
        _ => "The microphone could not be opened. Check it in Settings > System > Sound, then try again.",
    };

    /// <summary>The tooltip while dictating; the idle tooltip otherwise.</summary>
    public static string StateTooltip(DictationState state, string idleTooltip, string recordingTooltip) => state switch
    {
        DictationState.Recording => recordingTooltip,
        DictationState.Transcribing or DictationState.Inserting => "Naad: transcribing",
        _ => idleTooltip,
    };

    /// <summary>What to tell the user when transcription fails. Never includes a response body.</summary>
    public static string TranscriptionErrorMessage(TranscriptionError error) => error switch
    {
        TranscriptionError.Unauthorized => "The API key was rejected. Open Settings from the Naad menu to " +
            "enter a valid key.",
        TranscriptionError.RateLimited => "The transcription service's rate limit was reached. Wait a " +
            "moment, then try again.",
        TranscriptionError.TooLarge => "The recording was too large for the transcription service. Try a " +
            "shorter dictation.",
        TranscriptionError.ServerError => "The transcription service had a problem. Try again in a moment.",
        TranscriptionError.Rejected => "The transcription service refused the request. Check the base URL " +
            "and model in Settings.",
        TranscriptionError.Network => "Naad couldn't reach the transcription service. Check your internet " +
            "connection.",
        TranscriptionError.NoInternet => "No internet connection. Connect and try again, or download the " +
            "model in Settings to transcribe on this PC when offline.",
        TranscriptionError.ConnectionLost => "The connection was lost while transcribing. Try again.",
        TranscriptionError.Timeout => "The request timed out: the transcription service didn't answer in time. " +
            "Try again.",
        TranscriptionError.LocalFailed => "Transcribing on this PC failed. Download the model again in Settings, " +
            "or switch to the cloud service.",
        _ => "The transcription service sent an unexpected answer. Check the base URL in Settings.",
    };

    /// <summary>What to tell the user when a transcript is kept. Windows cuts it at 255 characters.</summary>
    public static string TranscriptKeptMessage(TranscriptKeptReason reason) => reason switch
    {
        TranscriptKeptReason.WindowChanged => "The window changed before your text was ready, so Naad didn't " +
            "paste it. Click here or choose Copy last transcript in the Naad menu, then paste it with Ctrl+V.",
        TranscriptKeptReason.TargetElevated => "Windows doesn't let Naad type into apps running as " +
            "administrator. Click here or choose Copy last transcript in the Naad menu, then paste with Ctrl+V.",
        TranscriptKeptReason.LiveTypingStopped => "Live typing stopped early, so part of your text may be " +
            "missing. Click here or choose Copy last transcript in the Naad menu for the full text.",
        _ => "Naad couldn't paste your text. Click here or choose Copy last transcript in the Naad menu, " +
            "then paste it with Ctrl+V.",
    };
}
