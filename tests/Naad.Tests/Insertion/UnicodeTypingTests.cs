using Naad.Hotkey;
using Naad.Insertion;

namespace Naad.Tests.Insertion;

public sealed class UnicodeTypingTests
{
    private const uint Unicode = 0x0004;
    private const uint KeyUp = 0x0002;
    private const ushort Return = 0x0D;

    [Fact]
    public void Build_Empty_HasNoKeys() => Assert.Empty(UnicodeTyping.Build(""));

    [Fact]
    public void Build_AsciiText_SendsEachCharacterDownThenUp()
    {
        var keys = Keys("hi");

        Assert.Equal(
            [(0, 'h', Unicode), (0, 'h', Unicode | KeyUp), (0, 'i', Unicode), (0, 'i', Unicode | KeyUp)], keys);
    }

    [Theory]
    [InlineData("é", 0x00E9)]
    [InlineData("न", 0x0928)]
    [InlineData("中", 0x4E2D)]
    public void Build_NonAsciiCharacter_SendsItsCodeUnit(string text, int unit) =>
        Assert.Equal([(0, (char)unit, Unicode), (0, (char)unit, Unicode | KeyUp)], Keys(text));

    [Fact]
    public void Build_Emoji_SendsBothSurrogatesInOrder()
    {
        // U+1F600 is the surrogate pair D83D DE00.
        var keys = Keys("\U0001F600");

        Assert.Equal(
            [
                (0, '\uD83D', Unicode), (0, '\uD83D', Unicode | KeyUp),
                (0, '\uDE00', Unicode), (0, '\uDE00', Unicode | KeyUp),
            ],
            keys);
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    public void Build_LineBreak_BecomesOneEnter(string text)
    {
        var keys = Keys(text);

        Assert.Equal(6, keys.Count);
        Assert.Equal((Return, '\0', 0u), keys[2]);
        Assert.Equal((Return, '\0', KeyUp), keys[3]);
    }

    [Fact]
    public void Build_EveryKey_CarriesNaadsMarker() =>
        Assert.All(
            UnicodeTyping.Build("a\nb"),
            input => Assert.Equal(KeyboardHook.OwnInputMarker, input.Union.Keyboard.ExtraInfo));

    private static List<(ushort Vk, char Scan, uint Flags)> Keys(string text) =>
        [.. UnicodeTyping.Build(text).Select(
            i => (i.Union.Keyboard.Vk, (char)i.Union.Keyboard.Scan, i.Union.Keyboard.Flags))];
}
