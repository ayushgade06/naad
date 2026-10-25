using System.Text.Json;
using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Tests.Hotkey;

public sealed class ChordTests
{
    [Theory]
    [InlineData("RightCtrl", new[] { Key.RControlKey })]
    [InlineData("rightctrl", new[] { Key.RControlKey })]
    [InlineData("LeftCtrl", new[] { Key.LControlKey })]
    [InlineData("LeftCtrl+Space", new[] { Key.LControlKey, Key.Space })]
    [InlineData(" RightAlt + RightShift ", new[] { Key.RMenu, Key.RShiftKey })]
    [InlineData("LeftWin+RightWin+F9", new[] { Key.LWin, Key.RWin, Key.F9 })]
    [InlineData("RControlKey", new[] { Key.RControlKey })]
    [InlineData("A+B+C+D", new[] { Key.A, Key.B, Key.C, Key.D })]
    public void TryParse_ValidText_ReturnsKeysInOrder(string text, Key[] expected)
    {
        Assert.True(Chord.TryParse(text, out var chord));
        Assert.Equal(expected, chord.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+")]
    [InlineData("RightCtrl+")]
    [InlineData("NoSuchKey")]
    [InlineData("65")]
    [InlineData("A, Control")]
    [InlineData("Control")]
    [InlineData("ControlKey")]
    [InlineData("ShiftKey")]
    [InlineData("Menu")]
    [InlineData("LButton")]
    [InlineData("None")]
    [InlineData("RightCtrl+RightCtrl")]
    [InlineData("RightCtrl+RControlKey")]
    [InlineData("A+B+C+D+E")]
    public void TryParse_InvalidText_Fails(string text)
    {
        Assert.False(Chord.TryParse(text, out var chord));
        Assert.Null(chord);
    }

    [Theory]
    [InlineData("RightCtrl")]
    [InlineData("LeftCtrl+LeftShift+Space")]
    [InlineData("LeftAlt+F9")]
    public void ToString_ParsedChord_RoundTrips(string text)
    {
        Assert.True(Chord.TryParse(text, out var chord));
        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void ToString_EnumModifierName_UsesFriendlyName()
    {
        Assert.True(Chord.TryParse("rcontrolkey+space", out var chord));
        Assert.Equal("RightCtrl+Space", chord.ToString());
    }

    [Theory]
    [InlineData("RightCtrl", "Right Ctrl")]
    [InlineData("LeftCtrl+Space", "Left Ctrl+Space")]
    [InlineData("RightWin+F9", "Right Windows+F9")]
    public void Describe_Chord_UsesReadableNames(string text, string expected)
    {
        Assert.True(Chord.TryParse(text, out var chord));
        Assert.Equal(expected, chord.Describe());
    }

    [Fact]
    public void Equals_SameKeysInAnotherOrder_IsEqual()
    {
        Assert.True(Chord.TryParse("LeftCtrl+Space", out var a));
        Assert.True(Chord.TryParse("Space+LeftCtrl", out var b));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_LeftAndRightModifier_AreDifferent()
    {
        Assert.True(Chord.TryParse("LeftCtrl", out var left));

        Assert.NotEqual(Chord.RightCtrl, left);
    }

    [Fact]
    public void Json_Chord_IsWrittenAsItsSettingsForm()
    {
        Assert.True(Chord.TryParse("LeftCtrl+Space", out var chord));

        var json = JsonSerializer.Serialize(chord);

        Assert.Equal("LeftCtrl+Space", JsonSerializer.Deserialize<string>(json));
        Assert.Equal(chord, JsonSerializer.Deserialize<Chord>(json));
    }

    [Theory]
    [InlineData("\"NoSuchKey\"")]
    [InlineData("163")]
    [InlineData("[\"RightCtrl\"]")]
    public void Json_InvalidValue_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Chord>(json));
    }
}
