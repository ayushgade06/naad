using Naad.Hotkey;
using Naad.Insertion;
using Key = System.Windows.Forms.Keys;

namespace Naad.Tests.Insertion;

public sealed class HeldModifiersTests
{
    private static readonly Chord LeftCtrlSpace = Parse("LeftCtrl+Space");
    private static readonly Chord RightAlt = Parse("RightAlt");

    [Fact]
    public void BlockTyping_ShortcutHeld_DoesNotBlock() =>
        Assert.False(HeldModifiers.BlockTyping([Key.RControlKey], [Chord.RightCtrl]));

    [Fact]
    public void BlockTyping_ChordWithSpaceHeld_DoesNotBlock() =>
        Assert.False(HeldModifiers.BlockTyping([Key.LControlKey], [LeftCtrlSpace]));

    [Theory]
    [InlineData(Key.LControlKey)]
    [InlineData(Key.LShiftKey)]
    [InlineData(Key.RMenu)]
    [InlineData(Key.LWin)]
    public void BlockTyping_OtherModifierHeld_Blocks(Key other) =>
        Assert.True(HeldModifiers.BlockTyping([Key.RControlKey, other], [Chord.RightCtrl]));

    [Fact]
    public void BlockTyping_AltShortcutHeld_Blocks() =>
        // Letting go of Alt alone opens the app's menu, so an Alt shortcut is waited out.
        Assert.True(HeldModifiers.BlockTyping([Key.RMenu], [RightAlt]));

    [Fact]
    public void BlockTyping_NothingHeld_DoesNotBlock() =>
        Assert.False(HeldModifiers.BlockTyping([], [Chord.RightCtrl]));

    [Fact]
    public void ToLift_ShortcutHeld_LiftsItsModifier() =>
        Assert.Equal([Key.RControlKey], HeldModifiers.ToLift([Key.RControlKey], [Chord.RightCtrl]));

    [Fact]
    public void ToLift_ShortcutAlreadyUp_LiftsNothing() =>
        Assert.Empty(HeldModifiers.ToLift([], [Chord.RightCtrl]));

    [Fact]
    public void ToLift_ChordWithSpace_LiftsOnlyTheModifier() =>
        Assert.Equal([Key.LControlKey], HeldModifiers.ToLift([Key.LControlKey], [LeftCtrlSpace]));

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException(text);
}
