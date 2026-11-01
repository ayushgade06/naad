using Naad.Meetings;

namespace Naad.Tray;

/// <summary>The tray's meeting notes: the Start/Stop item, the indicator while recording, and what happened.</summary>
internal sealed partial class TrayIcon
{
    private const string MeetingTitle = "Naad meeting notes";
    private const string MeetingTooltip = "Naad: recording meeting notes";
    private ToolStripMenuItem? _meeting;

    /// <summary>Adds Start meeting notes / Stop meeting notes after the dictation item. Call once.</summary>
    public void AddMeetingItem(Action toggle)
    {
        _meeting = new ToolStripMenuItem("Start meeting notes", null, (_, _) => toggle());
        _menu.Items.Insert(_menu.Items.IndexOf(_toggleDictation) + 1, _meeting);
    }

    /// <summary>
    /// Shows that a meeting is being recorded, with the recording dot, or that it stopped. A
    /// meeting and a dictation never run at once, so the dot is never ambiguous.
    /// </summary>
    public void ShowMeetingRecording(bool recording)
    {
        if (_meeting is not null)
        {
            _meeting.Text = recording ? "Stop meeting notes" : "Start meeting notes";
        }

        _toggleDictation.Enabled = !recording;
        _icon.Icon = recording ? _recordingImage : _image;
        _icon.Text = recording ? MeetingTooltip : _idleTooltip;
    }

    /// <summary>Says how starting or stopping a meeting went; Started says nothing.</summary>
    public void ShowMeetingOutcome(MeetingOutcome outcome)
    {
        if (MeetingMessage(outcome) is { } message)
        {
            Notify(MeetingTitle, message, outcome is MeetingOutcome.Copied ? ToolTipIcon.Info : ToolTipIcon.Warning,
                clicked: null);
        }
    }

    /// <summary>The microphone stopped mid-meeting, as when it is unplugged.</summary>
    public void ShowMeetingMicrophoneLost() => Notify(
        MeetingTitle,
        "Naad lost the microphone. The meeting goes on with the PC's sound only; stop and start again to use " +
            "another microphone.",
        ToolTipIcon.Warning,
        clicked: null);

    /// <summary>A press of the dictation shortcut was refused while a meeting records.</summary>
    public void ShowDictationRefusedDuringMeeting() => Notify(
        MeetingTitle,
        "Meeting notes are recording, so dictation is off until you stop them.",
        ToolTipIcon.Info,
        clicked: null);

    /// <summary>What to say for <paramref name="outcome"/>, or null for nothing.</summary>
    public static string? MeetingMessage(MeetingOutcome outcome) => outcome switch
    {
        MeetingOutcome.DictationRunning => "Finish the dictation first, then start meeting notes.",
        MeetingOutcome.NoTranscriber => "Set up transcription in Settings first.",
        MeetingOutcome.CaptureFailed =>
            "Naad couldn't open the microphone or the PC's sound. Check the sound settings and try again.",
        MeetingOutcome.Copied => "The meeting's transcript is saved and copied to the clipboard.",
        MeetingOutcome.CopiedNotSaved => "Copied to the clipboard, but Naad couldn't save this meeting.",
        MeetingOutcome.SavedNotCopied => "The meeting is saved on the Meetings page; the clipboard was busy.",
        MeetingOutcome.NotSaved => "Naad couldn't save or copy this meeting.",
        MeetingOutcome.NothingHeard => "No speech was picked up, so nothing was saved.",
        _ => null,
    };
}
