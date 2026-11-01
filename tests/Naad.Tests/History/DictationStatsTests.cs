using System.Globalization;
using Naad.History;

namespace Naad.Tests.History;

public sealed class DictationStatsTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello there.", 2)]
    [InlineData("Don't stop — it's well-known, 2026!", 5)]
    [InlineData("  spaced   out  ", 2)]
    public void CountWords_Text_CountsLikeAReader(string text, int expected)
    {
        Assert.Equal(expected, DictationStats.CountWords(text));
    }

    [Fact]
    public void Compute_Records_SumsWordsPaceAndTimeSaved()
    {
        HistoryRecord[] records =
        [
            Record(Today, "one two three four five six seven eight nine ten", audioMs: 6_000),
            Record(Today, "one two three four five six seven eight nine ten", audioMs: 6_000),
            Record(Today, "ignored because it failed", audioMs: 6_000, HistoryOutcome.Failed),
        ];

        var stats = DictationStats.Compute(records, Today, TimeZoneInfo.Utc);

        Assert.Equal(20, stats.Words);
        Assert.Equal(100, stats.WordsPerMinute);
        Assert.Equal(TimeSpan.FromMinutes((20 / 40.0) - 0.2), stats.TimeSaved);
    }

    [Fact]
    public void Compute_NoTimedAudio_HasNoPace()
    {
        var stats = DictationStats.Compute([Record(Today, "a b c", audioMs: 0)], Today, TimeZoneInfo.Utc);

        Assert.Equal((3, 0), (stats.Words, stats.WordsPerMinute));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, 3)]
    [InlineData(new[] { 1, 2 }, 2)]
    [InlineData(new[] { 0, 2 }, 1)]
    [InlineData(new[] { 2, 3 }, 0)]
    [InlineData(new int[0], 0)]
    public void Compute_Days_CountsTheStreakEndingTodayOrYesterday(int[] daysAgo, int streak)
    {
        var records = daysAgo.Select(d => Record(Today.AddDays(-d), "hi", 1_000));

        Assert.Equal(streak, DictationStats.Compute(records, Today, TimeZoneInfo.Utc).DayStreak);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Yesterday")]
    [InlineData(3, "Thursday")]
    [InlineData(10, "Thursday, Oct 22")]
    [InlineData(400, "Sep 27, 2025")]
    public void DayLabel_Age_ReadsNaturally(int daysAgo, string expected)
    {
        Assert.Equal(expected, DictationStats.DayLabel(Today.AddDays(-daysAgo), Today, CultureInfo.InvariantCulture));
    }

    private static HistoryRecord Record(
        DateOnly day, string text, long audioMs, HistoryOutcome outcome = HistoryOutcome.Pasted) => new(
        Guid.NewGuid(),
        new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        "notepad",
        audioMs,
        text,
        text,
        100,
        0,
        outcome,
        false);
}
