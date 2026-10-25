using Naad.Hotkey;

namespace Naad.Tray;

/// <summary>Naad's notification-area icon and menu. Create and dispose it on the UI thread.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Icon _image = LoadIcon();
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;

    public TrayIcon(Chord shortcut, Action exit)
    {
        _menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = _image, Text = Tooltip(shortcut), ContextMenuStrip = _menu, Visible = true };
    }

    /// <summary>
    /// Also the icon's accessible name, which UI tests use to find it. NotifyIcon refuses
    /// text over 127 characters; a chord's four-key limit keeps it well under that.
    /// </summary>
    public static string Tooltip(Chord shortcut) => $"Naad: hold {shortcut.Describe()} to dictate";

    /// <summary>Loads the embedded icon at the size Windows uses in the notification area.</summary>
    public static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Naad.ico")
            ?? throw new InvalidOperationException("The embedded Naad.ico resource is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
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
