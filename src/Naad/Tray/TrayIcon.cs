using System.Diagnostics;
using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;

namespace Naad.Tray;

/// <summary>Naad's notification-area icon and menu. Create and dispose it on the UI thread.</summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Title of the notification shown when a dictation can't record.</summary>
    public const string MicrophoneErrorTitle = "Naad can't use the microphone";

    /// <summary>Title of the notification shown when a transcript is kept instead of inserted.</summary>
    public const string TranscriptKeptTitle = "Naad kept your text";

    /// <summary>The menu item that puts a kept transcript on the clipboard.</summary>
    public const string CopyLastTranscriptText = "Copy last transcript";

    private const string CopyFailedMessage = "The clipboard is busy. Try Copy last transcript again.";

    // Windows' microphone privacy page; the URI is documented as ms-settings:privacy-microphone.
    private const string MicrophonePrivacySettings = "ms-settings:privacy-microphone";

    // Windows 11 shows balloon tips as toasts and ignores the timeout; Windows 10 uses it.
    private const int NotificationMilliseconds = 10_000;

    private readonly Icon _image = LoadIcon();
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _copyLastTranscript;
    private readonly Func<Task<bool>> _copy;
    private Action? _notificationClicked;

    /// <param name="shortcut">Named in the tooltip.</param>
    /// <param name="copyLastTranscript">Copies the kept transcript; false if it couldn't.</param>
    /// <param name="exit">Ends the app.</param>
    public TrayIcon(Chord shortcut, Func<Task<bool>> copyLastTranscript, Action exit)
    {
        _copy = copyLastTranscript;
        _copyLastTranscript = new ToolStripMenuItem(CopyLastTranscriptText, null, (_, _) => _ = CopyAsync())
        {
            Enabled = false,
        };
        _menu.Items.Add(_copyLastTranscript);
        _menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = _image, Text = Tooltip(shortcut), ContextMenuStrip = _menu, Visible = true };
        _icon.BalloonTipClicked += (_, _) => _notificationClicked?.Invoke();
    }

    /// <summary>
    /// Also the icon's accessible name, which UI tests use to find it. NotifyIcon refuses
    /// text over 127 characters; a chord's four-key limit keeps it well under that.
    /// </summary>
    public static string Tooltip(Chord shortcut) => $"Naad: hold {shortcut.Describe()} to dictate";

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

    /// <summary>What to tell the user when a transcript is kept. Windows cuts it at 255 characters.</summary>
    public static string TranscriptKeptMessage(TranscriptKeptReason reason) => reason switch
    {
        TranscriptKeptReason.WindowChanged => "The window changed before your text was ready, so Naad didn't " +
            "paste it. Click here or choose Copy last transcript in the Naad menu, then paste it with Ctrl+V.",
        TranscriptKeptReason.TargetElevated => "Windows doesn't let Naad type into apps running as " +
            "administrator. Click here or choose Copy last transcript in the Naad menu, then paste with Ctrl+V.",
        _ => "Naad couldn't paste your text. Click here or choose Copy last transcript in the Naad menu, " +
            "then paste it with Ctrl+V.",
    };

    /// <summary>Loads the embedded icon at the size Windows uses in the notification area.</summary>
    public static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Naad.ico")
            ?? throw new InvalidOperationException("The embedded Naad.ico resource is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// <summary>
    /// Tells the user why a dictation couldn't record. For a privacy block, clicking the
    /// notification opens the microphone privacy settings.
    /// </summary>
    public void ShowMicrophoneError(MicrophoneError error)
    {
        _notificationClicked = error == MicrophoneError.Blocked ? OpenPrivacySettings : null;
        _icon.ShowBalloonTip(
            NotificationMilliseconds, MicrophoneErrorTitle, MicrophoneErrorMessage(error), ToolTipIcon.Warning);
    }

    /// <summary>
    /// Tells the user their text was kept, and enables Copy last transcript. Clicking the
    /// notification copies it too.
    /// </summary>
    public void ShowTranscriptKept(TranscriptKeptReason reason)
    {
        _copyLastTranscript.Enabled = true;
        _notificationClicked = () => _ = CopyAsync();
        _icon.ShowBalloonTip(
            NotificationMilliseconds, TranscriptKeptTitle, TranscriptKeptMessage(reason), ToolTipIcon.Info);
    }

    private static void OpenPrivacySettings() =>
        Process.Start(new ProcessStartInfo(MicrophonePrivacySettings) { UseShellExecute = true })?.Dispose();

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task CopyAsync()
    {
        try
        {
            if (!await _copy())
            {
                _notificationClicked = null;
                _icon.ShowBalloonTip(
                    NotificationMilliseconds, TranscriptKeptTitle, CopyFailedMessage, ToolTipIcon.Warning);
            }
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.TranscriptCopyFailed, e);
        }
    }

    public void Dispose()
    {
        // Hiding first removes the icon at once; otherwise a dead icon can linger in the
        // notification area until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
    }
}
