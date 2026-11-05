using System.Globalization;
using System.Text.Json;

namespace Naad.History;

/// <summary>One local day's totals. Counts only: never what was said.</summary>
/// <param name="Words">Words pasted or kept.</param>
/// <param name="AudioMilliseconds">Speaking time of those dictations.</param>
/// <param name="Dictations">How many there were.</param>
internal sealed record DayCounts(int Words, long AudioMilliseconds, int Dictations);

/// <summary>
/// The lifetime log behind the Home page's stats: a count per local day. It outlives the history,
/// so Delete all and retention leave the numbers alone, and it holds no text, so it is kept as plain
/// JSON. Pure: reading and writing the file is the caller's (see <see cref="HistoryRecorder"/>).
/// </summary>
internal sealed class LifetimeStats
{
    private const string DayFormat = "yyyy-MM-dd";

    private readonly SortedDictionary<DateOnly, DayCounts> _days;

    private LifetimeStats(SortedDictionary<DateOnly, DayCounts> days) => _days = days;

    public static LifetimeStats Empty => new([]);

    public IReadOnlyDictionary<DateOnly, DayCounts> Days => _days;

    /// <summary>
    /// Counts a finished dictation. Only text that went in or was kept counts; failed, empty and
    /// edit runs leave the totals as they were. Returns whether anything was counted.
    /// </summary>
    public bool Add(DateOnly day, HistoryOutcome outcome, bool isEdit, string text, long audioMilliseconds)
    {
        if (isEdit || outcome is not (HistoryOutcome.Pasted or HistoryOutcome.Kept))
        {
            return false;
        }

        var before = _days.GetValueOrDefault(day, new DayCounts(0, 0, 0));
        _days[day] = new(
            before.Words + DictationStats.CountWords(text),
            before.AudioMilliseconds + Math.Max(0, audioMilliseconds),
            before.Dictations + 1);
        return true;
    }

    /// <summary>The totals for the history that was kept before the log existed.</summary>
    public static LifetimeStats Seed(IEnumerable<HistoryRecord> records, TimeZoneInfo zone)
    {
        var stats = Empty;
        foreach (var r in records)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.Time, zone).DateTime);
            _ = stats.Add(day, r.Outcome, r.Selection is not null, r.Text, r.AudioMilliseconds);
        }

        return stats;
    }

    /// <summary>Words, pace, time saved and the streak ending <paramref name="today"/>.</summary>
    // ponytail: pace divides every word by all speaking time, so dictations kept without their
    // length (none since 2.0) would raise it; count timed words per day if that ever shows.
    public Stats Compute(DateOnly today)
    {
        var words = _days.Values.Sum(d => d.Words);
        var minutes = _days.Values.Sum(d => d.AudioMilliseconds) / 60_000.0;
        var wpm = minutes > 0 ? (int)Math.Round(words / minutes) : 0;
        var saved = TimeSpan.FromMinutes(Math.Max(0, (words / (double)DictationStats.TypingWordsPerMinute) - minutes));
        return new(words, wpm, saved, Streak(today));
    }

    /// <summary>The log as JSON: day → counts.</summary>
    public string ToJson() => JsonSerializer.Serialize(
        _days.ToDictionary(p => p.Key.ToString(DayFormat, CultureInfo.InvariantCulture), p => p.Value));

    /// <summary>The log in <paramref name="json"/>; null when it can't be read, so the caller can start over.</summary>
    public static LifetimeStats? FromJson(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, DayCounts>>(json);
            if (raw is null)
            {
                return null;
            }

            var days = new SortedDictionary<DateOnly, DayCounts>();
            foreach (var (key, counts) in raw)
            {
                if (!DateOnly.TryParseExact(key, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var day) || counts is null || counts.Words < 0 || counts.AudioMilliseconds < 0
                    || counts.Dictations < 0)
                {
                    return null;
                }

                days[day] = counts;
            }

            return new(days);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Days in a row with words, ending today, or yesterday while today has none yet.
    private int Streak(DateOnly today)
    {
        bool HasWords(DateOnly day) => _days.TryGetValue(day, out var counts) && counts.Words > 0;
        var day = HasWords(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (HasWords(day))
        {
            streak++;
            day = day.AddDays(-1);
        }

        return streak;
    }
}
