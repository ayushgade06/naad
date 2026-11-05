using Naad.Window;

namespace Naad.Tests.Window;

public sealed class GreetingTests
{
    [Theory]
    [InlineData("Ada Lovelace", "Ada")]
    [InlineData("Lovelace, Ada", "Ada")]
    [InlineData("  Ada  ", "Ada")]
    [InlineData("SYNTHETIC\\ada", null)]
    [InlineData("ada@example.com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Lovelace,", null)]
    public void FirstName_DisplayName_TakesTheGivenName(string? display, string? expected)
    {
        Assert.Equal(expected, Greeting.FirstName(display));
    }

    [Fact]
    public void Heading_NoName_WelcomesBack()
    {
        Assert.Equal("Welcome back, get back into it with", Greeting.Heading(null));
    }

    [Fact]
    public void Heading_Name_GreetsByName()
    {
        Assert.Equal("Hey Ada, get back into it with", Greeting.Heading("Ada"));
    }
}
