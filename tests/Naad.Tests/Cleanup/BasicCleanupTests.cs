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
    [InlineData("call 5 5 5 1 2 3 4", "Call 5 5 5 1 2 3 4.")]
    [InlineData("no, no, no", "No, no, no.")]
    [InlineData("first line\nsecond line", "First line\nsecond line.")]
    public void Clean_Transcript_AppliesTheRules(string transcript, string expected)
    {
        Assert.Equal(expected, BasicCleanup.Clean(transcript));
    }

    [Theory]
    [InlineData("we should meet", "We should meet.")]
    [InlineData("Dear team,\n\nthanks um", "Dear team,\n\nthanks um.")]
    [InlineData("Done?", "Done?")]
    [InlineData("  ", "")]
    public void Finish_ModelText_OnlyAddsTheCapitalAndStop(string text, string expected)
    {
        Assert.Equal(expected, BasicCleanup.Finish(text));
    }
}
