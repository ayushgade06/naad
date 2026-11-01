using System.Globalization;

namespace Naad.History;

/// <param name="Words">Words in everything pasted or kept.</param>
/// <param name="WordsPerMinute">Words per minute of recorded speech; 0 without timed recordings.</param>
/// <param name="TimeSaved">Typing time at <see cref="DictationStats.TypingWordsPerMinute"/> minus speaking
/// time.</param>
/// <param name="DayStreak">Days in a row, ending today or yesterday, with at least one dictation.</param>
internal readonly record struct Stats(int Words, int WordsPerMinute, TimeSpan TimeSaved, int DayStreak);

/// <summary>The Home page's numbers, from the history. Pure.</summary>
internal static class DictationStats
{
    /// <summary>A typical typing speed: the time saved is measured against it.</summary>
    public const int TypingWordsPerMinute = 40;

    /// <summary>
    /// The stats for <paramref name="records"/>. Failed dictations count for the streak only.
    /// Days are local days: <paramref name="today"/> and each record's time in <paramref name="zone"/>.
    /// </summary>
    public static Stats Compute(IEnumerable<HistoryRecord> records, DateOnly today, TimeZoneInfo zone)
    {
        var all = records.ToList();
        var used = all.Where(r => r.Outcome != HistoryOutcome.Failed).ToList();
        var words = used.Sum(r => CountWords(r.Text));
        var timed = used.Where(r => r.AudioMilliseconds > 0).ToList();
        var minutes = timed.Sum(r => r.AudioMilliseconds) / 60_000.0;
        var timedWords = timed.Sum(r => CountWords(r.Text));
        var wpm = minutes > 0 ? (int)Math.Round(timedWords / minutes) : 0;
        var saved = TimeSpan.FromMinutes(Math.Max(0, (words / (double)TypingWordsPerMinute) - minutes));
        return new(words, wpm, saved, Streak(all, today, zone));
    }

    /// <summary>Words as a reader counts them: runs of letters or digits, apostrophes kept inside.</summary>
    public static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            var part = char.IsLetterOrDigit(c) || (inWord && c is '\'' or '’' or '-');
            if (part && !inWord)
            {
                count++;
            }

            inWord = part;
        }

        return count;
    }

    /// <summary>A day's heading in the history: Today, Yesterday, a weekday this week, or the date.</summary>
    public static string DayLabel(DateOnly day, DateOnly today, CultureInfo culture)
    {
        var daysAgo = today.DayNumber - day.DayNumber;
        return daysAgo switch
        {
            0 => "Today",
            1 => "Yesterday",
            > 1 and < 7 => day.ToString("dddd", culture),
            _ when day.Year == today.Year => day.ToString("dddd, MMM d", culture),
            _ => day.ToString("MMM d, yyyy", culture),
        };
    }

    private static int Streak(List<HistoryRecord> records, DateOnly today, TimeZoneInfo zone)
    {
        var days = records
            .Select(r => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.Time, zone).DateTime))
            .ToHashSet();

        // A streak still counts this morning if yesterday had a dictation.
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}
