using System.Windows.Forms;
using Naad.Hotkey;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class TrayIconTests
{
    // Loads the embedded resource only; no icon is shown in the notification area.
    [Fact]
    public void LoadIcon_EmbeddedResource_MatchesSystemSmallIconSize()
    {
        using var icon = TrayIcon.LoadIcon();

        Assert.Equal(SystemInformation.SmallIconSize, icon.Size);
    }

    [Fact]
    public void Tooltip_DefaultShortcut_NamesRightCtrl()
    {
        Assert.Equal("Naad: hold Right Ctrl to dictate", TrayIcon.Tooltip(Chord.RightCtrl));
    }

    [Fact]
    public void Tooltip_LongestPossibleChord_FitsNotifyIconLimit()
    {
        // NotifyIcon.Text throws above 127 characters, which would stop Naad from starting.
        const int NotifyIconTextLimit = 127;
        var longest = Enum.GetNames<Keys>()
            .Select(name => Chord.TryParse(name, out var chord) ? chord : null)
            .OfType<Chord>()
            .DistinctBy(chord => chord.Keys[0])
            .OrderByDescending(chord => chord.Describe().Length)
            .Take(4)
            .Select(chord => chord.ToString());
        Assert.True(Chord.TryParse(string.Join('+', longest), out var widest));

        Assert.True(TrayIcon.Tooltip(widest).Length <= NotifyIconTextLimit, TrayIcon.Tooltip(widest));
    }
}
