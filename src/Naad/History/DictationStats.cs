using System.Globalization;

namespace Naad.History;

/// <param name="Words">Words in everything pasted or kept.</param>
/// <param name="WordsPerMinute">Words per minute of recorded speech; 0 without timed recordings.</param>
/// <param name="TimeSaved">Typing time at <see cref="DictationStats.TypingWordsPerMinute"/> minus speaking
/// time.</param>
/// <param name="DayStreak">Days in a row, ending today or yesterday, with words dictated.</param>
internal readonly record struct Stats(int Words, int WordsPerMinute, TimeSpan TimeSaved, int DayStreak);

/// <summary>Word counts and labels for the Home and History pages. Pure.</summary>
internal static class DictationStats
{
    /// <summary>A typical typing speed: the time saved is measured against it.</summary>
    public const int TypingWordsPerMinute = 40;

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

    /// <summary>A line for the Home page that fits a streak of <paramref name="days"/> days.</summary>
    public static string StreakMessage(int days) => days switch
    {
        <= 0 => "Dictate something today to start a streak.",
        1 => "Day one of your streak. Come back tomorrow to keep it going.",
        2 => "Two days in a row. A habit is forming.",
        <= 4 => $"{days} days in a row. Your voice is getting a workout.",
        <= 6 => $"{days} days running. Almost a full week.",
        <= 29 => $"{days} days in a row. Talking beats typing, and you know it.",
        _ => $"{days} days straight. Dictation is simply how you write now.",
    };
}
