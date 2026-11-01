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
    // Often enough that the offer comes a few seconds into a call.
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    // After an offer, no other for this long: one call that two apps join (a browser and the
    // call app, say) gets one offer, not two in a row.
    private static readonly TimeSpan OfferShown = TimeSpan.FromSeconds(20);

    /// <summary>Wires meeting notes up and returns what the window's Meetings page needs.</summary>
    /// <param name="controller">The dictation.</param>
    /// <param name="tray">The tray icon.</param>
    /// <param name="transcriber">The transcriber in use.</param>
    /// <param name="settings">The settings in use.</param>
    /// <param name="copy">Puts text on the clipboard, out of clipboard history.</param>
    /// <param name="languageModel">The language model in use, for summaries.</param>
    public static MeetingActions Connect(
        DictationController controller,
        TrayIcon tray,
        Func<ITranscriber?> transcriber,
        Func<AppSettings> settings,
        Func<string, Task<bool>> copy,
        Func<LanguageModel.ILanguageModel?> languageModel)
    {
        var store = new MeetingStore(MeetingStore.DefaultFolder);
        var recorder = new MeetingRecorder(
            store,
            transcriber,
            () => controller.State == DictationState.Idle,
            () => AudioRecorder.DeviceNumber(AudioRecorder.ConnectedNames(), settings().Microphone),
            () => settings().KeepMeetingAudio,
            copy);
        var actions = new MeetingActions(recorder, store, copy, languageModel);

        // Busy from opening the tracks until the last piece is saved, not only while recording.
        controller.UseBusy(() => recorder.IsBusy);
        var ui = SynchronizationContext.Current!;
        recorder.MicrophoneBlocked += blocked => ui.Post(_ => tray.ShowMeetingMicrophoneBlocked(blocked), null);
        controller.RefusedWhileBusy += tray.ShowDictationRefusedDuringMeeting;
        recorder.RecordingChanged += tray.ShowMeetingRecording;
        actions.Finished += tray.ShowMeetingOutcome;
        tray.AddMeetingItem(() => _ = actions.ToggleAsync());
        // The offer only ever starts notes: clicked late, it must never stop a meeting.
        WatchForCalls(recorder, tray, settings, () =>
        {
            if (!recorder.IsBusy)
            {
                _ = actions.ToggleAsync();
            }
        });
        return actions;
    }

    // Reads Windows' records of microphone use every couple of seconds, on this thread: a few
    // registry keys, read in well under a millisecond. Only the offer's click starts recording.
    // ponytail: the timer runs for the life of the app; nothing stops it before exit.
    private static void WatchForCalls(MeetingRecorder recorder, TrayIcon tray, Func<AppSettings> settings,
        Action takeNotes)
    {
        var detector = new MeetingDetector();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        TimeSpan? offered = null;
        var timer = new System.Windows.Forms.Timer { Interval = (int)PollEvery.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            var s = settings();
            if (s.MeetingDetection == MeetingDetection.Off)
            {
                return;
            }

            var now = System.Diagnostics.Stopwatch.GetElapsedTime(started);
            var app = detector.Next(
                MicrophoneUsage.Read(),
                now,
                busy: recorder.IsBusy || now - offered < OfferShown,
                s.MeetingIgnoredApps);
            if (app is not null)
            {
                offered = now;
                Log.Info(LogEvent.MeetingOffered);
                tray.ShowMeetingOffer(app, takeNotes);
            }
        };
        timer.Start();
    }
}
