using Naad.Cleanup;

namespace Naad.Tests.Cleanup;

public sealed class BasicCleanupTests
{
    [Theory]
    [InlineData("um so we should uh meet tomorrow", "So we should meet tomorrow.")]
    [InlineData("Um, I think, uh, yes", "I think, yes.")]
    [InlineData("we need the the budget", "We need the budget.")]
    [InlineData("I I I think so", "I think so.")]
    [InlineData("he had had enough", "He had had enough.")]
    [InlineData("is it ready?", "Is it ready?")]
    [InlineData("Done.", "Done.")]
    [InlineData("hmm erm", "")]
    [InlineData("", "")]
    [InlineData("the umbrella is here", "The umbrella is here.")]
    [InlineData("send it to the summary team", "Send it to the summary team.")]
    [InlineData("call 911", "Call 911.")]
    [InlineData("ok , so", "Ok, so.")]
    public void Clean_Transcript_AppliesTheRules(string transcript, string expected)
    {
        Assert.Equal(expected, BasicCleanup.Clean(transcript));
    }
}
