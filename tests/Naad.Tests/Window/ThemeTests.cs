using Naad.Window;

namespace Naad.Tests.Window;

public sealed class ThemeTests
{
    [Theory]
    [InlineData("System", true, false)]
    [InlineData("System", false, true)]
    [InlineData("Light", false, false)]
    [InlineData("Dark", true, true)]
    public void IsDark_Choice_FollowsWindowsOnlyForSystem(string choice, bool windowsLight, bool dark)
    {
        Assert.Equal(dark, Theme.IsDark(Enum.Parse<ThemeChoice>(choice), windowsLight));
        Assert.Same(dark ? Theme.Dark : Theme.Light, Theme.For(Enum.Parse<ThemeChoice>(choice), windowsLight));
    }

    [Fact]
    public void Palettes_TextStandsOutFromBackground()
    {
        foreach (var palette in new[] { Theme.Light, Theme.Dark })
        {
            Assert.True(Math.Abs(Luminance(palette.Text) - Luminance(palette.Background)) > 0.6);
        }
    }

    private static double Luminance(uint argb) =>
        ((((argb >> 16) & 0xFF) * 0.2126) + (((argb >> 8) & 0xFF) * 0.7152) + ((argb & 0xFF) * 0.0722)) / 255;
}
