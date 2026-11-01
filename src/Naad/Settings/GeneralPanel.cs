using Naad.Tray;
using Naad.Window;

using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>
/// The settings' General page: appearance, the tray icon, starting at sign-in, the log and the
/// sounds. Create and use it on the UI thread.
/// </summary>
internal sealed class GeneralPanel : TableLayoutPanel
{
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly CheckBox _showTrayIcon = new()
    {
        Text = "Show Naad in the notification area (when hidden, start Naad again to open this window)",
        AutoSize = true,
        MaximumSize = new Size(420, 0),
    };

    private readonly CheckBox _startAtLogin = new() { Text = "Start Naad when I sign in", AutoSize = true };
    private readonly CheckBox _fileLogging = new()
    {
        Text = "Write a diagnostic log (never what you say)",
        AutoSize = true,
    };

    private readonly CheckBox _playSounds = new()
    {
        Text = "Play a tone when listening starts and stops",
        AutoSize = true,
    };

    private readonly CheckBox _errorSound = new() { Text = "Play a low tone when a dictation fails", AutoSize = true };
    private readonly TrackBar _volume = new()
    {
        Minimum = 0,
        Maximum = 100,
        TickFrequency = 10,
        Width = 200,
    };

    private readonly Button _preview = new() { Text = "Preview", AutoSize = true };
    private readonly ComboBox _history = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly CheckBox _pressEnter = new()
    {
        Text = "Saying \u201cpress enter\u201d at the end presses Enter after the text",
        AutoSize = true,
    };

    private readonly CheckBox _timer = new() { Text = "Show a timer while recording", AutoSize = true };
    private readonly CheckBox _detectMeetings = new()
    {
        Text = "Offer meeting notes when a call starts (nothing records until you click)",
        AutoSize = true,
    };

    private readonly CheckBox _keepMeetingAudio = new()
    {
        Text = "Keep meeting recordings with their notes (encrypted)",
        AutoSize = true,
    };
    private SoundCues? _previewCues;

    /// <param name="current">The settings in use.</param>
    /// <param name="startsAtLogin">Whether Naad starts when the user signs in.</param>
    public GeneralPanel(AppSettings current, bool startsAtLogin)
    {
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);
        _theme.Items.AddRange(["Follow Windows", "Light", "Dark"]);
        _theme.SelectedIndex = (int)current.Theme;
        _showTrayIcon.Checked = current.ShowTrayIcon;
        _startAtLogin.Checked = startsAtLogin;
        _fileLogging.Checked = current.FileLogging;
        _playSounds.Checked = current.PlaySounds;
        _errorSound.Checked = current.ErrorSound;
        _volume.Value = current.SoundVolume;
        _history.Items.AddRange(["Off: keep nothing", "Keep 7 days", "Keep 30 days", "Keep until I delete"]);
        _history.SelectedIndex = (int)current.HistoryRetention;
        _pressEnter.Checked = current.PressEnterCommand;
        _timer.Checked = current.OverlayTimer;
        _keepMeetingAudio.Checked = current.KeepMeetingAudio;
        _detectMeetings.Checked = current.MeetingDetection == Naad.Meetings.MeetingDetection.Notify;

        Row(this, "Theme", _theme);
        Row(this, "", _showTrayIcon);
        Row(this, "", _startAtLogin);
        Row(this, "", _fileLogging);
        Row(this, "Sounds", _playSounds);
        Row(this, "", _errorSound);
        Row(this, "Volume", Flow(_volume, _preview));
        Row(this, "History", _history);
        Row(this, "", Note("Dictations are kept encrypted for your Windows account, and can be deleted on the " +
            "History page. Off deletes them all."));
        Row(this, "Meetings", _detectMeetings);
        Row(this, "", _keepMeetingAudio);
        Row(this, "", Note("Meeting notes are kept encrypted until you delete them on the Meetings page. " +
            "Without this, each piece of audio is dropped once it is transcribed."));
        Row(this, "", _pressEnter);
        Row(this, "", _timer);
        Row(this, "", Note("Esc cancels a dictation. Pressing Space while holding the hold-to-talk shortcut " +
            "keeps recording after you let go; press a shortcut again to stop."));
        Row(this, "", new Label
        {
            Text = "Off by default: on PCs whose speakers sleep, waking them can delay the start of a dictation.",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
        });

        // Its own player at the chosen volume: the app's cues keep theirs until saved. Played
        // without waiting, since this thread also runs the keyboard hook.
        _preview.Click += (_, _) =>
        {
            _previewCues?.Dispose();
            _previewCues = new SoundCues(_volume.Value);
            _previewCues.PlayStart();
        };
    }

    /// <summary>Whether to start at sign-in, as ticked.</summary>
    public bool StartAtLogin => _startAtLogin.Checked;

    /// <summary><paramref name="chosen"/> with this page's choices applied.</summary>
    public AppSettings? Apply(AppSettings chosen, out string? error)
    {
        error = null;
        return Apply(chosen) with
        {
            HistoryRetention = (Naad.History.HistoryRetention)_history.SelectedIndex,
            PressEnterCommand = _pressEnter.Checked,
            OverlayTimer = _timer.Checked,
            KeepMeetingAudio = _keepMeetingAudio.Checked,
            MeetingDetection = _detectMeetings.Checked
                ? Naad.Meetings.MeetingDetection.Notify
                : Naad.Meetings.MeetingDetection.Off,
        };
    }

    private AppSettings Apply(AppSettings chosen) => chosen with
    {
        Theme = (ThemeChoice)_theme.SelectedIndex,
        ShowTrayIcon = _showTrayIcon.Checked,
        FileLogging = _fileLogging.Checked,
        PlaySounds = _playSounds.Checked,
        ErrorSound = _errorSound.Checked,
        SoundVolume = _volume.Value,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _previewCues?.Dispose();
        }

        base.Dispose(disposing);
    }
}
