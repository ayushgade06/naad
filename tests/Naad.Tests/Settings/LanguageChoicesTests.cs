using Naad.Settings;

namespace Naad.Tests.Settings;

public sealed class LanguageChoicesTests
{
    [Theory]
    [InlineData("English (en)", "en")]
    [InlineData("  hi ", "hi")]
    [InlineData("haw", "haw")]
    [InlineData("", "")]
    [InlineData(LanguageChoices.Detect, "")]
    public void Code_ReadsThePickedOrTypedCode(string text, string expected) =>
        Assert.Equal(expected, LanguageChoices.Code(text));

    [Fact]
    public void Label_RoundTripsThroughCode()
    {
        Assert.Equal(LanguageChoices.Detect, LanguageChoices.Label(null));
        Assert.Equal("en", LanguageChoices.Code(LanguageChoices.Label("en")));
        Assert.All(LanguageChoices.All.Skip(1), label => Assert.EndsWith(")", label));
    }
}
