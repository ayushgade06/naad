using Naad.Live;

namespace Naad.Tests.Live;

public sealed class RealtimeTextTests
{
    [Fact]
    public void Apply_OpenAiUtterances_StableOnlyUpToTheFirstUnfinished()
    {
        var text = new RealtimeText();

        text.Apply(new(RealtimeEventKind.Committed, "a"));
        text.Apply(new(RealtimeEventKind.Committed, "b"));
        text.Apply(new(RealtimeEventKind.Partial, "a", "syn"));
        text.Apply(new(RealtimeEventKind.Partial, "a", "thetic"));
        Assert.Equal("", text.Stable);
        Assert.Equal("synthetic", text.Preview);

        // The second utterance finishes first; it isn't stable until the first is.
        Assert.True(text.Apply(new(RealtimeEventKind.Final, "b", "two.")));
        Assert.Equal("", text.Stable);
        Assert.False(text.Settled);

        text.Apply(new(RealtimeEventKind.Final, "a", "one."));
        Assert.Equal("one. two.", text.Stable);
        Assert.True(text.Settled);
    }

    [Fact]
    public void Apply_DeepgramResults_AppendsFinalsAndReplacesInterims()
    {
        var text = new RealtimeText();

        text.Apply(new(RealtimeEventKind.Partial, null, "syn"));
        text.Apply(new(RealtimeEventKind.Partial, null, "synthetic on"));
        Assert.Equal("synthetic on", text.Preview);
        Assert.False(text.Settled);

        text.Apply(new(RealtimeEventKind.Final, null, "Synthetic one."));
        text.Apply(new(RealtimeEventKind.Partial, null, "two"));
        Assert.Equal("Synthetic one.", text.Stable);
        Assert.Equal("Synthetic one. two", text.Preview);

        text.Apply(new(RealtimeEventKind.Final, null, "Two."));
        text.Apply(new(RealtimeEventKind.Done));
        Assert.Equal("Synthetic one. Two.", text.Stable);
        Assert.True(text.Complete);
    }

    [Fact]
    public void Apply_Error_Fails()
    {
        var text = new RealtimeText();

        text.Apply(new(RealtimeEventKind.Error));

        Assert.True(text.Failed);
    }

    [Fact]
    public void Apply_EmptyFinal_LeavesNoGap()
    {
        var text = new RealtimeText();

        text.Apply(new(RealtimeEventKind.Final, null, "One."));
        text.Apply(new(RealtimeEventKind.Final, null, ""));
        text.Apply(new(RealtimeEventKind.Final, null, "Two."));

        Assert.Equal("One. Two.", text.Stable);
    }

    [Fact]
    public void Complete_OpenAiFinalButEndNotAnswered_IsFalse()
    {
        var text = new RealtimeText();
        text.Apply(new(RealtimeEventKind.Committed, "a"));
        text.Apply(new(RealtimeEventKind.Final, "a", "Hello there."));

        // More speech followed, then the end was sent; the server hasn't answered yet.
        text.FinishSent();

        Assert.True(text.Settled);
        Assert.False(text.Complete);
    }

    [Fact]
    public void Complete_OpenAiCommitAnsweredAndFinal_IsTrue()
    {
        var text = new RealtimeText();
        text.Apply(new(RealtimeEventKind.Committed, "a"));
        text.Apply(new(RealtimeEventKind.Final, "a", "One."));
        text.FinishSent();

        text.Apply(new(RealtimeEventKind.Committed, "b"));
        Assert.False(text.Complete);
        text.Apply(new(RealtimeEventKind.Final, "b", "Two."));

        Assert.True(text.Complete);
        Assert.Equal("One. Two.", text.Stable);
    }

    [Fact]
    public void Complete_OpenAiNothingLeftToCommit_IsTrue()
    {
        var text = new RealtimeText();
        text.Apply(new(RealtimeEventKind.Committed, "a"));
        text.Apply(new(RealtimeEventKind.Final, "a", "One."));
        text.FinishSent();

        text.Apply(new(RealtimeEventKind.NothingToCommit));

        Assert.True(text.Complete);
    }

    [Fact]
    public void Closed_BeforeTheEndWasAnswered_Fails()
    {
        var text = new RealtimeText();
        text.Apply(new(RealtimeEventKind.Final, null, "One."));
        text.FinishSent();

        text.Closed();

        Assert.True(text.Failed);
        Assert.False(text.Complete);
    }

    [Fact]
    public void Closed_AfterDeepgramMetadata_StaysComplete()
    {
        var text = new RealtimeText();
        text.Apply(new(RealtimeEventKind.Final, null, "One."));
        text.FinishSent();
        text.Apply(new(RealtimeEventKind.Done));

        text.Closed();

        Assert.True(text.Complete);
    }
}
