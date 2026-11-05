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

    // After an offer, no other while it shows: one call that two apps join (a browser and the
    // call app, say) gets one offer, not two in a row.
    private static readonly TimeSpan OfferShown = MeetingPrompt.ShownFor;

    /// <summary>Wires meeting notes up and returns what the window's Meetings page needs.</summary>
    /// <param name="controller">The dictation.</param>
    /// <param name="tray">The tray icon.</param>
    /// <param name="transcriber">The transcriber in use.</param>
    /// <param name="settings">The settings in use.</param>
    /// <param name="copy">Puts text on the clipboard, out of clipboard history.</param>
    /// <param name="languageModel">The language model in use, for summaries.</param>
    /// <param name="hook">The keyboard hook, for the meeting shortcut.</param>
    /// <param name="sounds">The sound cues in use, or null.</param>
    public static MeetingActions Connect(
        DictationController controller,
        TrayIcon tray,
        Func<ITranscriber?> transcriber,
        Func<AppSettings> settings,
        Func<string, Task<bool>> copy,
        Func<LanguageModel.ILanguageModel?> languageModel,
        Hotkey.KeyboardHook hook,
        Func<SoundCues?> sounds)
    {
        var store = new MeetingStore(MeetingStore.DefaultFolder);
        var recorder = new MeetingRecorder(
            store,
            transcriber,
            () => controller.IsIdle,
            () => settings().Microphone,
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
        tray.AddMeetingItem(() => _ = actions.ToggleAsync(), () => settings().MeetingShortcut);

        // The same tones as dictation: start once both tracks are open, stop once they are closed.
        // ponytail: the PC's-sound track leaves Naad's tones out, but the microphone can hear the
        // start tone from the speakers; play it before opening the tracks if a beep shows up as text.
        void Play(bool wanted, Action<SoundCues> play)
        {
            if (wanted && sounds() is { } cues)
            {
                play(cues);
            }
        }

        recorder.RecordingChanged += recording => Play(settings().PlaySounds, recording
            ? s => s.PlayStart()
            : s => s.PlayStop());
        actions.Finished += outcome => Play(
            settings().ErrorSound && outcome is MeetingOutcome.CaptureFailed or MeetingOutcome.NoTranscriber
                or MeetingOutcome.DictationRunning or MeetingOutcome.NotSaved,
            s => s.PlayError());

        // The shortcut starts or stops notes; while a dictation runs, or a meeting is still finishing,
        // it only plays the error tone.
        hook.MeetingPressed += () =>
        {
            if (recorder.Finishing is not null || (!recorder.IsBusy && !controller.IsIdle))
            {
                Play(settings().ErrorSound, s => s.PlayError());
                return;
            }

            _ = actions.ToggleAsync();
        };

        // The offer only ever starts notes: clicked late, it must never stop a meeting.
        var prompt = new MeetingPrompt();
        Application.ThreadExit += (_, _) => prompt.Dispose();
        recorder.RecordingChanged += recording =>
        {
            if (recording)
            {
                prompt.Hide();
            }
        };
        WatchForCalls(recorder, prompt, settings, () =>
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
    private static void WatchForCalls(MeetingRecorder recorder, MeetingPrompt prompt, Func<AppSettings> settings,
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
                prompt.Offer(TrayIcon.MeetingAppName(app), takeNotes);
            }
        };
        timer.Start();
    }
}
