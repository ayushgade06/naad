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

    [Theory]
    [InlineData(0, "start a streak")]
    [InlineData(1, "Day one")]
    [InlineData(2, "Two days")]
    [InlineData(4, "4 days in a row. Your voice")]
    [InlineData(6, "Almost a full week")]
    [InlineData(29, "Talking beats typing")]
    [InlineData(30, "30 days straight")]
    public void StreakMessage_Days_FitsTheStreak(int days, string expected)
    {
        Assert.Contains(expected, DictationStats.StreakMessage(days), StringComparison.Ordinal);
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
}
