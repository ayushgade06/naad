using System.Windows.Forms;
using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Transcription;
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

        Assert.All(
            [
                TrayIcon.Tooltip(widest),
                TrayIcon.Tooltip(widest, handsFree: true),
                TrayIcon.RecordingTooltip(widest, handsFree: true),
            ],
            tooltip => Assert.True(tooltip.Length <= NotifyIconTextLimit, tooltip));
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

    [Fact]
    public void TranscriptKeptMessage_EveryReason_IsDistinctFitsAndPointsToCopy()
    {
        var messages = Enum.GetValues<TranscriptKeptReason>().Select(TrayIcon.TranscriptKeptMessage).ToList();

        Assert.Equal(messages.Count, messages.Distinct().Count());
        Assert.All(messages, message =>
        {
            Assert.InRange(message.Length, 1, 255);
            Assert.Contains(TrayIcon.CopyLastTranscriptText, message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TranscriptionErrorMessage_EveryError_IsDistinctAndFits()
    {
        var messages = Enum.GetValues<TranscriptionError>().Select(TrayIcon.TranscriptionErrorMessage).ToList();

        Assert.Equal(messages.Count, messages.Distinct().Count());
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 255));
    }

    [Fact]
    public void TranscriptionErrorMessage_Unauthorized_PointsToSettings() =>
        Assert.Contains(
            "Open Settings",
            TrayIcon.TranscriptionErrorMessage(TranscriptionError.Unauthorized),
            StringComparison.Ordinal);

    [Fact]
    public void UpdateAvailableMessage_LongVersion_FitsNotificationLimitAndNamesIt()
    {
        var message = TrayIcon.UpdateAvailableMessage("123.456.789-preview.1+abcdef");

        Assert.InRange(message.Length, 1, 255);
        Assert.Contains("123.456.789", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiKeyNeededMessage_FitsNotificationLimit() =>
        Assert.InRange(TrayIcon.ApiKeyNeededMessage.Length, 1, 255);

    [Theory]
    [InlineData("Recording", "recording tooltip")]
    [InlineData("Transcribing", "Naad: transcribing")]
    [InlineData("Inserting", "Naad: transcribing")]
    [InlineData("Idle", "idle tooltip")]
    public void StateTooltip_EachState_DescribesIt(string state, string expected) =>
        Assert.Equal(
            expected,
            TrayIcon.StateTooltip(Enum.Parse<DictationState>(state), "idle tooltip", "recording tooltip"));

    [Theory]
    [InlineData(false, "Naad: hold Right Ctrl to dictate", "Naad: listening. Release to transcribe.")]
    [InlineData(true, "Naad: press Right Ctrl to start and stop dictating",
        "Naad: listening. Press Right Ctrl again to transcribe.")]
    public void Tooltips_EachMode_SayHowToDictate(bool handsFree, string idle, string recording)
    {
        Assert.Equal(idle, TrayIcon.Tooltip(Chord.RightCtrl, handsFree));
        Assert.Equal(recording, TrayIcon.RecordingTooltip(Chord.RightCtrl, handsFree));
    }
}
