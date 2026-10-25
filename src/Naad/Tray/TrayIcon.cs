using System.Diagnostics;
using Naad.Audio;
using Naad.Hotkey;

namespace Naad.Tray;

/// <summary>Naad's notification-area icon and menu. Create and dispose it on the UI thread.</summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Title of the notification shown when a dictation can't record.</summary>
    public const string MicrophoneErrorTitle = "Naad can't use the microphone";

    // Windows' microphone privacy page; the URI is documented as ms-settings:privacy-microphone.
    private const string MicrophonePrivacySettings = "ms-settings:privacy-microphone";

    // Windows 11 shows balloon tips as toasts and ignores the timeout; Windows 10 uses it.
    private const int NotificationMilliseconds = 10_000;

    private readonly Icon _image = LoadIcon();
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private bool _notificationOpensPrivacySettings;

    public TrayIcon(Chord shortcut, Action exit)
    {
        _menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = _image, Text = Tooltip(shortcut), ContextMenuStrip = _menu, Visible = true };
        _icon.BalloonTipClicked += (_, _) =>
        {
            if (_notificationOpensPrivacySettings)
            {
                Process.Start(new ProcessStartInfo(MicrophonePrivacySettings) { UseShellExecute = true })?.Dispose();
            }
        };
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
        _notificationOpensPrivacySettings = error == MicrophoneError.Blocked;
        _icon.ShowBalloonTip(
            NotificationMilliseconds, MicrophoneErrorTitle, MicrophoneErrorMessage(error), ToolTipIcon.Warning);
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
