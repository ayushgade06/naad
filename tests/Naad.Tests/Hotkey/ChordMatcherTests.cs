using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Tests.Hotkey;

// Steps are written as "+Key" for key-down and "-Key" for key-up, using chord key names
// (RightCtrl, LeftShift, C), and "Mouse" for a mouse button or wheel. Expected events are
// "Pressed:<chord>", "Released:<chord>" or "Cancelled:<chord>", with the chord in its settings form.
public sealed class ChordMatcherTests
{
    [Theory]
    // Right Ctrl alone.
    [InlineData("+RightCtrl", "Pressed:RightCtrl")]
    [InlineData("+RightCtrl -RightCtrl", "Pressed:RightCtrl Released:RightCtrl")]
    [InlineData("+RightCtrl -RightCtrl +RightCtrl -RightCtrl",
        "Pressed:RightCtrl Released:RightCtrl Pressed:RightCtrl Released:RightCtrl")]
    // Right Ctrl plus another key cancels; the later release reports nothing (M-S3 step 2).
    [InlineData("+RightCtrl +C -C -RightCtrl", "Pressed:RightCtrl Cancelled:RightCtrl")]
    [InlineData("+RightCtrl +C -RightCtrl -C", "Pressed:RightCtrl Cancelled:RightCtrl")]
    [InlineData("+RightCtrl +LeftShift -LeftShift -RightCtrl", "Pressed:RightCtrl Cancelled:RightCtrl")]
    // A cancelled chord stays inactive until one of its keys is pressed again.
    [InlineData("+RightCtrl +C -C -RightCtrl +RightCtrl", "Pressed:RightCtrl Cancelled:RightCtrl Pressed:RightCtrl")]
    // Auto-repeat: Windows repeats the key-down of a held key; only the first counts.
    [InlineData("+RightCtrl +RightCtrl +RightCtrl -RightCtrl", "Pressed:RightCtrl Released:RightCtrl")]
    [InlineData("+RightCtrl +C +C +C -C -RightCtrl", "Pressed:RightCtrl Cancelled:RightCtrl")]
    // Left Ctrl is a different key (M-S3 step 1).
    [InlineData("+LeftCtrl -LeftCtrl", "")]
    [InlineData("+LeftCtrl +C -C -LeftCtrl", "")]
    [InlineData("+LeftCtrl +RightCtrl -RightCtrl -LeftCtrl", "Pressed:RightCtrl Released:RightCtrl")]
    // Keys already held when the chord's key goes down don't block it.
    [InlineData("+LeftShift +RightCtrl -RightCtrl -LeftShift", "Pressed:RightCtrl Released:RightCtrl")]
    // A key released that was never seen going down (held before the hook started).
    [InlineData("-RightCtrl -C", "")]
    // Other keys alone do nothing.
    [InlineData("+A -A +Space -Space", "")]
    public void OnKey_RightCtrlChord_EmitsExpectedEvents(string steps, string expected)
    {
        Assert.Equal(expected, Run(["RightCtrl"], steps));
    }

    [Theory]
    // Every key must be held; the chord presses on the key that completes it, in either order.
    [InlineData("+LeftCtrl +Space", "Pressed:LeftCtrl+Space")]
    [InlineData("+Space +LeftCtrl", "Pressed:LeftCtrl+Space")]
    [InlineData("+LeftCtrl -LeftCtrl +Space -Space", "")]
    // Releasing keys out of order: whichever key comes up first releases the chord.
    [InlineData("+LeftCtrl +Space -LeftCtrl -Space", "Pressed:LeftCtrl+Space Released:LeftCtrl+Space")]
    [InlineData("+LeftCtrl +Space -Space -LeftCtrl", "Pressed:LeftCtrl+Space Released:LeftCtrl+Space")]
    // A release never activates a chord, even if the keys still held would match again.
    [InlineData("+LeftCtrl +Space -Space +Space",
        "Pressed:LeftCtrl+Space Released:LeftCtrl+Space Pressed:LeftCtrl+Space")]
    [InlineData("+LeftCtrl +Space +X -X -Space", "Pressed:LeftCtrl+Space Cancelled:LeftCtrl+Space")]
    // Right Ctrl is not Left Ctrl.
    [InlineData("+RightCtrl +Space", "")]
    public void OnKey_TwoKeyChord_EmitsExpectedEvents(string steps, string expected)
    {
        Assert.Equal(expected, Run(["LeftCtrl+Space"], steps));
    }

    [Theory]
    // The most specific matching chord wins.
    [InlineData("+RightShift +RightCtrl", "Pressed:RightCtrl+RightShift")]
    // Growing into the longer chord cancels the shorter one first.
    [InlineData("+RightCtrl +RightShift", "Pressed:RightCtrl Cancelled:RightCtrl Pressed:RightCtrl+RightShift")]
    [InlineData("+RightCtrl +RightShift -RightShift -RightCtrl",
        "Pressed:RightCtrl Cancelled:RightCtrl Pressed:RightCtrl+RightShift Released:RightCtrl+RightShift")]
    // Leaving the longer chord does not fall back to the shorter one.
    [InlineData("+RightCtrl +RightShift -RightShift +C",
        "Pressed:RightCtrl Cancelled:RightCtrl Pressed:RightCtrl+RightShift Released:RightCtrl+RightShift")]
    // A third key cancels the longer chord and matches nothing.
    [InlineData("+RightShift +RightCtrl +C", "Pressed:RightCtrl+RightShift Cancelled:RightCtrl+RightShift")]
    public void OnKey_NestedChords_MostSpecificWins(string steps, string expected)
    {
        Assert.Equal(expected, Run(["RightCtrl", "RightCtrl+RightShift"], steps));
    }

    [Fact]
    public void OnKey_ChordsOfEqualLength_FirstInListWins()
    {
        // Pressing F9 last completes both chords at once.
        Assert.Equal("Pressed:LeftCtrl+F9", Run(["LeftCtrl+F9", "LeftAlt+F9"], "+LeftCtrl +LeftAlt +F9"));
        Assert.Equal("Pressed:LeftAlt+F9", Run(["LeftAlt+F9", "LeftCtrl+F9"], "+LeftCtrl +LeftAlt +F9"));
    }

    [Fact]
    public void OnKey_NoChords_EmitsNothing()
    {
        Assert.Equal("", Run([], "+RightCtrl -RightCtrl"));
    }

    [Theory]
    // A mouse button or the wheel cancels the active chord (Ctrl+click, Ctrl+scroll).
    [InlineData("+RightCtrl Mouse -RightCtrl", "Pressed:RightCtrl Cancelled:RightCtrl")]
    [InlineData("+RightCtrl Mouse Mouse", "Pressed:RightCtrl Cancelled:RightCtrl")]
    // Without an active chord the mouse does nothing, and the next press works.
    [InlineData("Mouse +RightCtrl -RightCtrl", "Pressed:RightCtrl Released:RightCtrl")]
    [InlineData("+RightCtrl Mouse -RightCtrl +RightCtrl",
        "Pressed:RightCtrl Cancelled:RightCtrl Pressed:RightCtrl")]
    [InlineData("+RightCtrl -RightCtrl Mouse", "Pressed:RightCtrl Released:RightCtrl")]
    public void OnMouseButton_WhileChordHeld_Cancels(string steps, string expected)
    {
        Assert.Equal(expected, Run(["RightCtrl"], steps));
    }

    [Theory]
    // Modifier-only chords pass every key through, so Right Ctrl keeps working as Ctrl.
    [InlineData("RightCtrl", "+RightCtrl +RightCtrl +C -C -RightCtrl", "p p p p p")]
    // The key that completes the chord is swallowed, with its repeats and its key-up.
    [InlineData("LeftCtrl+Space", "+LeftCtrl +Space +Space -Space -LeftCtrl", "p s s s p")]
    [InlineData("LeftCtrl+Space", "+LeftCtrl +Space -LeftCtrl -Space", "p s p s")]
    // Pressed before the modifier, Space already reached the app, so its key-up must too.
    [InlineData("LeftCtrl+Space", "+Space +LeftCtrl -LeftCtrl -Space", "p p p p")]
    // A swallowed key stays swallowed until it comes up, even after a cancel.
    [InlineData("LeftCtrl+Space", "+LeftCtrl +Space +X +Space -X -Space", "p s p s p s")]
    [InlineData("LeftCtrl+Space", "+LeftCtrl +Space Mouse +Space -Space", "p s - s s")]
    // Space on its own is not the chord, so it is typed normally.
    [InlineData("LeftCtrl+Space", "+Space +Space -Space", "p p p")]
    [InlineData("F9", "+F9 +F9 -F9 +A -A", "s s s p p")]
    public void OnKey_ChordKeys_SwallowsOnlyNonModifiersOfActiveChord(string chord, string steps, string expected)
    {
        var matcher = new ChordMatcher([Parse(chord)]);

        var outcomes = steps.Split(' ').Select(step => step == "Mouse"
            ? Mouse(matcher)
            : matcher.OnKey(Parse(step[1..]).Keys.Single(), down: step[0] == '+').Swallow ? "s" : "p");

        Assert.Equal(expected, string.Join(' ', outcomes));
    }

    [Fact]
    public void IsActive_FollowsTheChord()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);

        matcher.OnKey(Key.RControlKey, down: true);
        Assert.True(matcher.IsActive);
        matcher.OnMouseButton();
        Assert.False(matcher.IsActive);
    }

    [Fact]
    public void SetChords_NewChord_MatchesOnlyTheNewOne()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);

        matcher.SetChords([Parse("LeftCtrl+Space")]);

        Assert.Empty(matcher.OnKey(Key.RControlKey, down: true).Events);
        matcher.OnKey(Key.RControlKey, down: false);
        matcher.OnKey(Key.LControlKey, down: true);
        Assert.Equal(ChordEventKind.Pressed, Assert.Single(matcher.OnKey(Key.Space, down: true).Events).Kind);
    }

    [Fact]
    public void SetChords_WhileChordActive_EndsItWithoutEvents()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.OnKey(Key.RControlKey, down: true);

        matcher.SetChords([Chord.RightCtrl]);

        Assert.False(matcher.IsActive);
        Assert.Empty(matcher.OnKey(Key.RControlKey, down: false).Events);
    }

    [Fact]
    public void SetChords_WhileSwallowedKeyHeld_StillSwallowsItsKeyUp()
    {
        var matcher = new ChordMatcher([Parse("F9")]);
        matcher.OnKey(Key.F9, down: true);

        matcher.SetChords([Chord.RightCtrl]);

        Assert.True(matcher.OnKey(Key.F9, down: false).Swallow);
    }

    [Theory]
    // Capture never raises dictation events, whatever is pressed, and swallows every key.
    [InlineData("RightCtrl", "+RightCtrl -RightCtrl")]
    [InlineData("RightCtrl", "+RightCtrl +C -C -RightCtrl")]
    [InlineData("LeftCtrl+Space", "+LeftCtrl +Space -Space -LeftCtrl")]
    [InlineData("RightCtrl", "+Escape -Escape")]
    public void OnKey_WhileCapturing_RaisesNoEventsAndSwallowsKeys(string chord, string steps)
    {
        var matcher = new ChordMatcher([Parse(chord)]);
        matcher.BeginCapture();

        var outcomes = steps.Split(' ')
            .Select(step => matcher.OnKey(Parse(step[1..]).Keys.Single(), down: step[0] == '+'))
            .ToList();

        Assert.All(outcomes, outcome => Assert.Empty(outcome.Events));
        Assert.All(outcomes, outcome => Assert.True(outcome.Swallow));
        Assert.False(matcher.IsCapturing);
    }

    [Fact]
    public void OnKey_AfterCapture_ChordsMatchAgain()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.BeginCapture();
        matcher.OnKey(Key.F9, down: true);

        var captured = matcher.OnKey(Key.F9, down: false).Capture;

        Assert.Equal(new CaptureStep(CaptureStatus.Captured, Parse("F9")), captured);
        Assert.Equal(ChordEventKind.Pressed, Assert.Single(matcher.OnKey(Key.RControlKey, down: true).Events).Kind);
    }

    [Fact]
    public void OnKey_CapturedKeyStillHeldAfterCapture_KeyUpStaysSwallowed()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.BeginCapture();
        matcher.OnKey(Key.LControlKey, down: true);
        matcher.OnKey(Key.Space, down: true);
        matcher.OnKey(Key.Space, down: false);

        var lateKeyUp = matcher.OnKey(Key.LControlKey, down: false);

        Assert.True(lateKeyUp.Swallow);
        Assert.Null(lateKeyUp.Capture);
    }

    [Fact]
    public void OnKey_KeyHeldBeforeCapture_ItsKeyUpReachesApps()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.OnKey(Key.LShiftKey, down: true);
        matcher.BeginCapture();

        Assert.False(matcher.OnKey(Key.LShiftKey, down: false).Swallow);
    }

    [Fact]
    public void CancelCapture_MidCapture_ChordsMatchAgain()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.BeginCapture();
        matcher.OnKey(Key.A, down: true);

        matcher.CancelCapture();

        Assert.True(matcher.OnKey(Key.A, down: false).Swallow);
        Assert.Equal(ChordEventKind.Pressed, Assert.Single(matcher.OnKey(Key.RControlKey, down: true).Events).Kind);
    }

    [Fact]
    public void BeginCapture_WhileChordActive_EndsItWithoutEvents()
    {
        var matcher = new ChordMatcher([Chord.RightCtrl]);
        matcher.OnKey(Key.RControlKey, down: true);

        matcher.BeginCapture();

        Assert.False(matcher.IsActive);
        Assert.Empty(matcher.OnKey(Key.RControlKey, down: false).Events);
    }

    private static string Mouse(ChordMatcher matcher)
    {
        matcher.OnMouseButton();
        return "-";
    }

    private static string Run(string[] chords, string steps)
    {
        var matcher = new ChordMatcher([.. chords.Select(Parse)]);
        var events = steps.Split(' ', StringSplitOptions.RemoveEmptyEntries).SelectMany(step => step == "Mouse"
            ? matcher.OnMouseButton()
            : matcher.OnKey(Parse(step[1..]).Keys.Single(), down: step[0] == '+').Events);
        return string.Join(' ', events.Select(e => $"{e.Kind}:{e.Chord}"));
    }

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException($"Bad chord in test: {text}");
}
