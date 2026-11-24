using System.Globalization;
using System.Text.RegularExpressions;

namespace Naad.LanguageModel;

/// <summary>How long a 429 asks the client to wait, from its headers. Pure.</summary>
internal static partial class RateLimitHeaders
{
    /// <summary>Used when a 429 names no wait: a per-minute limit has passed by then.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);

    // OpenAI's request counter is per minute: an empty one is a daily quota only when its reset
    // is this far off.
    private static readonly TimeSpan DailyAtLeast = TimeSpan.FromHours(1);

    /// <summary>
    /// The wait, and whether it is the daily request quota running out. Reads, in order: the
    /// request quota's reset when no requests remain (daily), retry-after, then the token quota's
    /// reset. Names are matched in any casing.
    /// </summary>
    public static (TimeSpan Wait, bool Daily) Read(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var values = headers.ToDictionary(
            h => h.Key, h => h.Value.FirstOrDefault() ?? "", StringComparer.OrdinalIgnoreCase);
        string? Get(string name) => values.TryGetValue(name, out var value) ? value.Trim() : null;

        if (Get("x-ratelimit-remaining-requests") is { } remaining
            && int.TryParse(remaining, CultureInfo.InvariantCulture, out var left) && left <= 0
            && Duration(Get("x-ratelimit-reset-requests")) is { } daily && daily >= DailyAtLeast)
        {
            return (daily, true);
        }

        return (Duration(Get("retry-after")) ?? Duration(Get("x-ratelimit-reset-tokens")) ?? DefaultWait, false);
    }

    // No provider asks for a longer wait in one header; a bigger number is garbage, and past
    // TimeSpan's range it would throw.
    private static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);

    /// <summary>
    /// "30" (seconds), "7.66s", "120ms", "2m59.56s" or "1h2m", as providers write them; null
    /// for anything else, or for a wait over a day.
    /// </summary>
    public static TimeSpan? Duration(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return Within(seconds);
        }

        var parts = Part().Matches(text);
        if (parts.Count == 0 || parts.Sum(p => p.Length) != text.Length)
        {
            return null;
        }

        var total = 0.0;
        foreach (Match part in parts)
        {
            var amount = double.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
            total += amount * part.Groups[2].Value switch
            {
                "h" => 3600,
                "m" => 60,
                "ms" => 0.001,
                _ => 1,
            };
        }

        return Within(total);
    }

    // NaN fails both comparisons.
    private static TimeSpan? Within(double seconds) =>
        seconds >= 0 && seconds <= LongestWait.TotalSeconds ? TimeSpan.FromSeconds(seconds) : null;

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|h|m|s)")]
    private static partial Regex Part();
}
