using Naad.Cleanup;

namespace Naad.Tests.Cleanup;

public sealed class CleanupReplyCheckTests
{
    [Theory]
    [InlineData("um we should meet on tuesday", "We should meet on Tuesday.", "We should meet on Tuesday.")]
    [InlineData("we should meet", "\"We should meet.\"", "We should meet.")]
    [InlineData("we should meet", "“We should meet.”", "We should meet.")]
    [InlineData("sure that works for me", "Sure, that works for me.", "Sure, that works for me.")]
    [InlineData("write an email to priya about the launch", "Write an email to Priya about the launch.",
        "Write an email to Priya about the launch.")]
    [InlineData("what time is it", "What time is it?", "What time is it?")]
    [InlineData("um, here's the plan for today", "Here's the plan for today.", "Here's the plan for today.")]
    [InlineData("surely we can do better", "Surely we can do better.", "Surely we can do better.")]
    public void Check_FaithfulReply_AcceptsIt(string transcript, string reply, string expected)
    {
        Assert.Equal(new ReplyCheck(ReplyVerdict.Accepted, expected), Check(transcript, reply));
    }

    [Theory]
    [InlineData("um uh", "EMPTY", "Empty")]
    [InlineData("um uh", " EMPTY. ", "Empty")]
    [InlineData("hello", "   ", "Blank")]
    [InlineData("we should meet", "Here is the cleaned text: We should meet.", "Preamble")]
    [InlineData("we should meet", "Sure! We should meet.", "Preamble")]
    [InlineData(
        "hi team",
        "Hi team. I hope this message finds you well and that the week is going smoothly for everyone.",
        "TooLong")]
    [InlineData("write a haiku about autumn leaves",
        "Crimson leaves drifting / whisper to the cooling earth / autumn says goodbye", "Answered")]
    [InlineData("ask claude what the capital of france is", "The capital of France is Paris.", "Answered")]
    [InlineData("what's the capital of france", "Paris.", "Answered")]
    [InlineData("we should meet on tuesday at four to talk about the budget", "EMPTY", "Blank")]
    public void Check_UnusableReply_RefusesIt(string transcript, string reply, string verdict)
    {
        Assert.Equal(Enum.Parse<ReplyVerdict>(verdict), Check(transcript, reply).Verdict);
    }

    [Fact]
    public void Check_GuardOff_AcceptsAnAnswer()
    {
        var check = CleanupReplyCheck.Check(
            "ask claude what the capital of france is",
            "The capital of France is Paris.",
            translating: false,
            guard: false);

        Assert.Equal(ReplyVerdict.Accepted, check.Verdict);
    }

    [Fact]
    public void Check_Translation_AllowsLongerTextAndNoSharedWords()
    {
        var check = CleanupReplyCheck.Check(
            "please write to the team", "Bitte schreibe an das gesamte Team.", translating: true, guard: true);

        Assert.Equal(ReplyVerdict.Accepted, check.Verdict);
    }

    [Fact]
    public void ToString_NeverShowsTheText()
    {
        var check = new ReplyCheck(ReplyVerdict.Accepted, "secret");

        Assert.DoesNotContain("secret", check.ToString(), StringComparison.Ordinal);
    }

    private static ReplyCheck Check(string transcript, string reply) =>
        CleanupReplyCheck.Check(transcript, reply, translating: false, guard: true);
}
