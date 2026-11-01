using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Tests.Hotkey;

// Esc, the hands-free latch and a second (paste-again) chord.
public sealed class ChordMatcherExtrasTests
{
    [Fact]
    public void Escape_Armed_IsReportedAndKeptFromTheApp()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]) { EscapeArmed = true };

        var down = matcher.OnKey(Key.Escape, down: true);
        var up = matcher.OnKey(Key.Escape, down: false);

        Assert.Equal([ChordEventKind.Escape], down.Events.Select(e => e.Kind));
        Assert.True(down.Swallow && up.Swallow);
    }

    [Fact]
    public void Escape_NotArmed_ReachesTheApp()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);

        var down = matcher.OnKey(Key.Escape, down: true);

        Assert.Empty(down.Events);
        Assert.False(down.Swallow);
    }

    [Fact]
    public void Space_WhileHeld_LatchesAndTheReleaseIsSilent()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.OnKey(Key.RControlKey, down: true);

        var space = matcher.OnKey(Key.Space, down: true);
        var spaceUp = matcher.OnKey(Key.Space, down: false);
        var release = matcher.OnKey(Key.RControlKey, down: false);

        Assert.Equal([ChordEventKind.Latched], space.Events.Select(e => e.Kind));
        Assert.True(space.Swallow && spaceUp.Swallow);
        Assert.Empty(release.Events);
    }

    [Fact]
    public void Space_InTheChord_IsNotALatch()
    {
        Assert.True(Chord.TryParse("LeftCtrl+Space", out var chord));
        var matcher = new ChordMatcher([chord]);
        matcher.OnKey(Key.LControlKey, down: true);

        Assert.Equal([ChordEventKind.Pressed], matcher.OnKey(Key.Space, down: true).Events.Select(e => e.Kind));
    }

    [Fact]
    public void OtherKey_WhileHeld_StillCancels()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.OnKey(Key.RControlKey, down: true);

        Assert.Equal([ChordEventKind.Cancelled], matcher.OnKey(Key.C, down: true).Events.Select(e => e.Kind));
    }

    [Fact]
    public void SecondChord_Pressed_IsReportedAsItself()
    {
        Assert.True(Chord.TryParse("RightAlt+V", out var paste));
        var matcher = new ChordMatcher([Chord.RightCtrl, paste]);
        matcher.OnKey(Key.RMenu, down: true);

        var events = matcher.OnKey(Key.V, down: true).Events;

        Assert.Equal([(ChordEventKind.Pressed, paste)], events.Select(e => (e.Kind, e.Chord)));
    }

    [Fact]
    public void Escape_ArmedWithTheChordHeld_EndsTheDictationNotJustTheHold()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]) { EscapeArmed = true };
        matcher.OnKey(Key.RControlKey, down: true);

        var down = matcher.OnKey(Key.Escape, down: true);

        Assert.Equal([ChordEventKind.Escape], down.Events.Select(e => e.Kind));
        Assert.True(down.Swallow);
        Assert.Empty(matcher.OnKey(Key.RControlKey, down: false).Events);
    }

    [Fact]
    public void Space_NotArmed_CancelsAndReachesTheApp()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]) { LatchArmed = false };
        matcher.OnKey(Key.RControlKey, down: true);

        var space = matcher.OnKey(Key.Space, down: true);

        Assert.Equal([ChordEventKind.Cancelled], space.Events.Select(e => e.Kind));
        Assert.False(space.Swallow);
    }
}
