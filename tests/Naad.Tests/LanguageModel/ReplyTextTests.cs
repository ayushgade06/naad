using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

public sealed class ReplyTextTests
{
    [Theory]
    [InlineData("Hello there.", "Hello there.")]
    [InlineData("  Hello there. \n", "Hello there.")]
    [InlineData("<think>The user wants a cleanup.</think>\n\nHello there.", "Hello there.")]
    [InlineData("<THINK>x</THINK>Hello", "Hello")]
    [InlineData("<think>a</think>Hello <think>b</think>there", "Hello there")]
    [InlineData("Hello <think>still reasoning when the tokens ran out", "Hello")]
    [InlineData("<think>only reasoning", "")]
    [InlineData("reasoning whose opening tag the server stripped</think>Hello", "Hello")]
    [InlineData("", "")]
    public void Clean_Reply_DropsReasoningAndTrims(string reply, string expected)
    {
        Assert.Equal(expected, ReplyText.Clean(reply));
    }
}
