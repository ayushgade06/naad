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

        Row(this, "Theme", _theme);
        Row(this, "", _showTrayIcon);
        Row(this, "", _startAtLogin);
        Row(this, "", _fileLogging);
        Row(this, "Sounds", _playSounds);
        Row(this, "", _errorSound);
        Row(this, "Volume", Flow(_volume, _preview));
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
    public AppSettings Apply(AppSettings chosen) => chosen with
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
