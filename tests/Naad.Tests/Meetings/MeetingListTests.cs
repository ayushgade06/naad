using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MeetingListTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(98.9, "01:38")]
    [InlineData(3725, "1:02:05")]
    public void Clock_ShowsMinutesOrHours(double seconds, string expected) =>
        Assert.Equal(expected, MeetingList.Clock(seconds));

    [Fact]
    public void SummaryLine_CountsSpeakersAndShowsTheLength()
    {
        var both = Meeting([new(Speaker.You, 0, "Hi."), new(Speaker.Others, 2, "Hello.")], TimeSpan.FromSeconds(98));
        var one = Meeting([new(Speaker.You, 0, "Hi.")], TimeSpan.FromSeconds(5));

        Assert.Equal("2 speakers · 01:38", MeetingList.SummaryLine(both));
        Assert.Equal("1 speaker · 00:05", MeetingList.SummaryLine(one));
    }

    [Theory]
    [InlineData(2026, 10, 21, "Today")]
    [InlineData(2026, 10, 20, "Yesterday")]
    [InlineData(2026, 10, 19, "Monday, Oct 19")]
    [InlineData(2025, 10, 20, "Monday, Oct 20, 2025")]
    public void DayLabel_NamesRecentDays(int year, int month, int day, string expected) =>
        Assert.Equal(expected, MeetingList.DayLabel(new DateOnly(year, month, day), new DateOnly(2026, 10, 21)));

    [Fact]
    public void ByDay_GroupsNewestFirst()
    {
        var older = Meeting([], TimeSpan.Zero) with { Started = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero) };
        var morning = Meeting([], TimeSpan.Zero) with { Started = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero) };
        var evening = Meeting([], TimeSpan.Zero) with { Started = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero) };

        var days = MeetingList.ByDay([older, morning, evening], new DateOnly(2026, 10, 21), Utc);

        Assert.Equal(["Today", "Yesterday"], days.Select(d => d.Day));
        Assert.Equal([evening, morning], days[0].Meetings);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("BUDGET", true)]
    [InlineData("planning", true)]
    [InlineData("roadmap", true)]
    [InlineData("elephant", false)]
    public void Matches_SearchesTitleSummaryAndWords(string query, bool expected)
    {
        var meeting = Meeting([new(Speaker.Others, 0, "The synthetic budget is due.")], TimeSpan.Zero) with
        {
            Title = "Planning",
            Summary = "## Summary\n- Roadmap",
        };

        Assert.Equal(expected, MeetingList.Matches(meeting, query));
    }

    [Fact]
    public void Markdown_HasTitleSummaryAndTimestampedTurns()
    {
        var meeting = Meeting(
            [new(Speaker.You, 0, "Synthetic hello."), new(Speaker.Others, 65, "Synthetic reply.")],
            TimeSpan.FromSeconds(70)) with
        { Title = "Planning", Summary = "## Summary\n- A synthetic point." };

        var markdown = MeetingList.Markdown(meeting, Utc).ReplaceLineEndings("\n");

        Assert.StartsWith("# Planning\n\n2026-10-21 15:04 · 2 speakers · 01:10\n\n## Summary\n", markdown);
        Assert.Contains("## Transcript\n\n**[00:00] You:** Synthetic hello.\n\n**[01:05] Others:** Synthetic reply.\n",
            markdown);
    }

    [Theory]
    [InlineData("One. Two? Three!", new[] { "One.", "Two?", "Three!" })]
    [InlineData("No ending", new[] { "No ending" })]
    [InlineData("3.5 percent rose.", new[] { "3.5 percent rose." })]
    public void Sentences_SplitAfterEndingPunctuation(string text, string[] expected) =>
        Assert.Equal(expected, MeetingList.Sentences(text));

    [Theory]
    [InlineData("  Planning  ", "Planning")]
    [InlineData("   ", null)]
    [InlineData("Old title", null)]
    public void Renamed_TrimsAndIgnoresBlankOrUnchanged(string typed, string? expected) =>
        Assert.Equal(expected, (Meeting([], TimeSpan.Zero) with { Title = "Old title" }).Renamed(typed));

    [Fact]
    public void Renamed_LongTitle_IsCutTo120() =>
        Assert.Equal(MeetingRecord.MaxTitleLength, Meeting([], TimeSpan.Zero).Renamed(new string('a', 200))!.Length);

    private static MeetingRecord Meeting(IReadOnlyList<MeetingSegment> segments, TimeSpan duration) =>
        new(Guid.NewGuid(), "Meeting", new(2026, 10, 21, 15, 4, 0, TimeSpan.Zero), duration, segments);
}
