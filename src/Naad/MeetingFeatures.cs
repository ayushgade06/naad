using Naad.Audio;
using Naad.Dictation;
using Naad.Meetings;
using Naad.Settings;
using Naad.Transcription;
using Naad.Tray;

namespace Naad;

/// <summary>
/// Connects meeting notes to the tray, the dictation and the window. A meeting and a dictation
/// never record at once: each refuses to start while the other runs. Every handler runs on the
/// UI thread.
/// </summary>
internal static class MeetingFeatures
{
    /// <summary>Wires meeting notes up and returns what the window's Meetings page needs.</summary>
    /// <param name="controller">The dictation.</param>
    /// <param name="tray">The tray icon.</param>
    /// <param name="transcriber">The transcriber in use.</param>
    /// <param name="settings">The settings in use.</param>
    /// <param name="copy">Puts text on the clipboard, out of clipboard history.</param>
    public static MeetingActions Connect(
        DictationController controller,
        TrayIcon tray,
        Func<ITranscriber?> transcriber,
        Func<AppSettings> settings,
        Func<string, Task<bool>> copy)
    {
        var store = new MeetingStore(MeetingStore.DefaultFolder);
        var recorder = new MeetingRecorder(
            store,
            transcriber,
            () => controller.State == DictationState.Idle,
            () => AudioRecorder.DeviceNumber(AudioRecorder.ConnectedNames(), settings().Microphone),
            () => settings().KeepMeetingAudio,
            copy);
        var actions = new MeetingActions(recorder, store);

        // Busy from opening the tracks until the last piece is saved, not only while recording.
        controller.UseBusy(() => recorder.IsBusy);
        var ui = SynchronizationContext.Current!;
        recorder.MicrophoneLost += () => ui.Post(_ => tray.ShowMeetingMicrophoneLost(), null);
        controller.RefusedWhileBusy += tray.ShowDictationRefusedDuringMeeting;
        recorder.RecordingChanged += tray.ShowMeetingRecording;
        actions.Finished += tray.ShowMeetingOutcome;
        tray.AddMeetingItem(() => _ = actions.ToggleAsync());
        return actions;
    }
}
