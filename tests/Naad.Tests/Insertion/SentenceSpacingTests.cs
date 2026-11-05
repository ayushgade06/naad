using Naad.Insertion;

namespace Naad.Tests.Insertion;

public sealed class SentenceSpacingTests
{
    [Theory]
    [InlineData("synthetic end.", "synthetic end. ")]
    [InlineData("synthetic end!", "synthetic end! ")]
    [InlineData("synthetic end?", "synthetic end? ")]
    [InlineData("synthetic end", "synthetic end")]
    [InlineData("synthetic end,", "synthetic end,")]
    [InlineData("", "")]
    public void AfterSentence_AddsASpaceOnlyAfterSentenceEnds(string text, string expected) =>
        Assert.Equal(expected, SentenceSpacing.AfterSentence(text));
}
