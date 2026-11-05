using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MeetingCardTests
{
    [Fact]
    public void Describe_Idle_IsHidden()
    {
        Assert.False(MeetingCard.Describe(false, false, false, TimeSpan.Zero, 0).Visible);
    }

    [Fact]
    public void Describe_RecordingWithoutLines_SaysItIsListening()
    {
        var card = MeetingCard.Describe(true, false, false, TimeSpan.FromSeconds(247), 0);

        Assert.Equal(new MeetingCardState(true, "4:07", null, MeetingCard.Listening, false), card);
    }

    [Fact]
    public void Describe_OverAnHour_ShowsHours()
    {
        Assert.Equal("1:02:30", MeetingCard.Describe(true, false, false, new TimeSpan(1, 2, 30), 3).Clock);
    }

    [Fact]
    public void Describe_Blocked_ShowsTheBanner()
    {
        Assert.Equal(MeetingCard.Blocked, MeetingCard.Describe(true, false, true, TimeSpan.Zero, 1).Banner);
    }

    [Fact]
    public void Describe_Finishing_SaysSoInsteadOfBlocked()
    {
        var card = MeetingCard.Describe(false, true, true, TimeSpan.FromSeconds(5), 3);

        Assert.Equal(
            (true, MeetingCard.Writing, null, true), (card.Visible, card.Banner, card.Placeholder, card.Finishing));
    }

    [Fact]
    public void Describe_Lines_HaveNoPlaceholder()
    {
        Assert.Null(MeetingCard.Describe(true, false, false, TimeSpan.Zero, 2).Placeholder);
    }
}
