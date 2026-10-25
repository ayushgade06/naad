using Naad.Hotkey;

namespace Naad.Tests.Hotkey;

// Steps are written as "+Key" for key-down and "-Key" for key-up, using chord key names
// (RightCtrl, LeftShift, C). Expected events are "Pressed:<chord>", "Released:<chord>" or
// "Cancelled:<chord>", with the chord in its settings form.
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

    private static string Run(string[] chords, string steps)
    {
        var matcher = new ChordMatcher([.. chords.Select(Parse)]);
        var events = steps.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(step => matcher.OnKey(Parse(step[1..]).Keys.Single(), down: step[0] == '+'));
        return string.Join(' ', events.Select(e => $"{e.Kind}:{e.Chord}"));
    }

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException($"Bad chord in test: {text}");
}
