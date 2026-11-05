using Naad.Dictation;
using Naad.Hotkey;
using Naad.Meetings;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class TrayStatusTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(nameof(DictationState.Recording), "Recording… (Naad 2.2.0)")]
    [InlineData(nameof(DictationState.Transcribing), "Transcribing… (Naad 2.2.0)")]
    [InlineData(nameof(DictationState.Inserting), "Transcribing… (Naad 2.2.0)")]
    [InlineData(nameof(DictationState.Idle), "Naad 2.2.0: hold Right Ctrl to dictate")]
    public void StatusLine_State_SaysWhatNaadIsDoing(string state, string expected)
    {
        var line = TrayIcon.StatusLine(Enum.Parse<DictationState>(state), "Naad: hold Right Ctrl to dictate", "2.2.0");

        Assert.Equal(expected, line);
    }

    [Fact]
    public void PasteAgainLabel_Shortcut_IsNamed()
    {
        Assert.True(Chord.TryParse("LeftCtrl+V", out var chord));

        Assert.Equal("Paste again (Left Ctrl+V): “synthetic”", TrayIcon.PasteAgainLabel("synthetic", chord));
    }

    [Fact]
    public void PasteAgainLabel_NoShortcutNoText_IsPlain()
    {
        Assert.Equal("Paste again", TrayIcon.PasteAgainLabel(null, null));
    }

    [Fact]
    public void Add_MoreThanKept_KeepsTheNewestFirst()
    {
        IReadOnlyList<RecentError> errors = [];
        foreach (var title in new[] { "a", "b", "c", "d" })
        {
            errors = RecentErrors.Add(errors, new(title, Noon));
        }

        Assert.Equal(["d", "c", "b"], errors.Select(e => e.Title));
    }

    [Fact]
    public void Add_RepeatOfTheNewest_IsOneLineWithTheNewerTime()
    {
        IReadOnlyList<RecentError> errors = [new("a", Noon), new("b", Noon)];

        errors = RecentErrors.Add(errors, new("a", Noon.AddMinutes(1)));

        Assert.Equal([new("a", Noon.AddMinutes(1)), new("b", Noon)], errors);
    }

    [Fact]
    public void Add_OlderRepeat_IsKeptAsANewLine()
    {
        IReadOnlyList<RecentError> errors = [new("b", Noon), new("a", Noon)];

        errors = RecentErrors.Add(errors, new("a", Noon));

        Assert.Equal(["a", "b", "a"], errors.Select(e => e.Title));
    }

    [Fact]
    public void MeetingMessage_NoEchoCancellation_Warns() =>
        Assert.Contains("echo cancellation", TrayIcon.MeetingMessage(MeetingOutcome.StartedWithoutEchoCancellation)!);
}
