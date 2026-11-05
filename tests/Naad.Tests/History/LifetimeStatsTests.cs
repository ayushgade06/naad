using Naad.History;

namespace Naad.Tests.History;

public sealed class LifetimeStatsTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void Compute_Seeded_SumsWordsPaceAndTimeSaved()
    {
        HistoryRecord[] records =
        [
            Record(Today, "one two three four five six seven eight nine ten", audioMs: 6_000),
            Record(Today, "one two three four five six seven eight nine ten", audioMs: 6_000),
            Record(Today, "ignored because it failed", audioMs: 6_000, HistoryOutcome.Failed),
            Record(Today, "ignored because it was empty", audioMs: 6_000, HistoryOutcome.Empty),
        ];

        var stats = LifetimeStats.Seed(records, TimeZoneInfo.Utc).Compute(Today);

        Assert.Equal(20, stats.Words);
        Assert.Equal(100, stats.WordsPerMinute);
        Assert.Equal(TimeSpan.FromMinutes((20 / 40.0) - 0.2), stats.TimeSaved);
    }

    [Fact]
    public void Add_Edit_IsLeftOut()
    {
        var stats = LifetimeStats.Empty;

        Assert.False(stats.Add(Today, HistoryOutcome.Pasted, isEdit: true, "synthetic edit", 1_000));
        Assert.Empty(stats.Days);
    }

    [Fact]
    public void Compute_NoAudio_HasNoPace()
    {
        var stats = LifetimeStats.Empty;
        stats.Add(Today, HistoryOutcome.Kept, isEdit: false, "a b c", 0);

        Assert.Equal((3, 0), (stats.Compute(Today).Words, stats.Compute(Today).WordsPerMinute));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, 3)]
    [InlineData(new[] { 1, 2 }, 2)]
    [InlineData(new[] { 0, 2 }, 1)]
    [InlineData(new[] { 2, 3 }, 0)]
    [InlineData(new int[0], 0)]
    public void Compute_Days_CountsTheStreakEndingTodayOrYesterday(int[] daysAgo, int streak)
    {
        var stats = LifetimeStats.Seed(daysAgo.Select(d => Record(Today.AddDays(-d), "hi", 1_000)), TimeZoneInfo.Utc);

        Assert.Equal(streak, stats.Compute(Today).DayStreak);
    }

    [Fact]
    public void Compute_DayWithoutWords_BreaksTheStreak()
    {
        var stats = LifetimeStats.Empty;
        stats.Add(Today, HistoryOutcome.Pasted, isEdit: false, "synthetic", 1_000);
        stats.Add(Today.AddDays(-1), HistoryOutcome.Pasted, isEdit: false, "", 1_000);
        stats.Add(Today.AddDays(-2), HistoryOutcome.Pasted, isEdit: false, "synthetic", 1_000);

        Assert.Equal(1, stats.Compute(Today).DayStreak);
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        var stats = LifetimeStats.Empty;
        stats.Add(Today, HistoryOutcome.Pasted, isEdit: false, "one two", 2_000);
        stats.Add(Today.AddDays(-1), HistoryOutcome.Kept, isEdit: false, "three", 500);

        var back = LifetimeStats.FromJson(stats.ToJson())!;

        Assert.Equal(stats.Days, back.Days);
        Assert.Equal(new DayCounts(2, 2_000, 1), back.Days[Today]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"yesterday":{"Words":1,"AudioMilliseconds":0,"Dictations":1}}""")]
    [InlineData("null")]
    [InlineData("""{"2026-11-01":{"Words":-1,"AudioMilliseconds":0,"Dictations":1}}""")]
    public void FromJson_Unreadable_IsNull(string json)
    {
        Assert.Null(LifetimeStats.FromJson(json));
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
