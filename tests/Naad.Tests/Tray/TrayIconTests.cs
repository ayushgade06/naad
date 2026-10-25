using System.Windows.Forms;
using Naad.Audio;
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

    [Fact]
    public void MicrophoneErrorMessage_EveryError_FitsNotificationLimit()
    {
        // Windows cuts notification text at 255 characters.
        const int NotificationTextLimit = 255;
        foreach (var error in Enum.GetValues<MicrophoneError>())
        {
            var message = TrayIcon.MicrophoneErrorMessage(error);

            Assert.InRange(message.Length, 1, NotificationTextLimit);
        }
    }

    [Fact]
    public void MicrophoneErrorMessage_Blocked_TellsUserToClickForSettings()
    {
        var message = TrayIcon.MicrophoneErrorMessage(MicrophoneError.Blocked);

        Assert.Contains("Click here", message, StringComparison.Ordinal);
        Assert.Contains("Let desktop apps access your microphone", message, StringComparison.Ordinal);
    }

    [Fact]
    public void MicrophoneErrorMessage_EachError_IsDistinct()
    {
        var messages = Enum.GetValues<MicrophoneError>().Select(TrayIcon.MicrophoneErrorMessage).ToList();

        Assert.Equal(messages.Count, messages.Distinct().Count());
    }
}
