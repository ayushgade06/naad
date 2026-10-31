using System.Globalization;
using System.Text.RegularExpressions;

namespace Naad.LanguageModel;

/// <summary>How long a 429 asks the client to wait, from its headers. Pure.</summary>
internal static partial class RateLimitHeaders
{
    /// <summary>Used when a 429 names no wait: a per-minute limit has passed by then.</summary>
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);

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
            && Duration(Get("x-ratelimit-reset-requests")) is { } daily)
        {
            return (daily, true);
        }

        return (Duration(Get("retry-after")) ?? Duration(Get("x-ratelimit-reset-tokens")) ?? DefaultWait, false);
    }

    /// <summary>
    /// "30" (seconds), "7.66s", "120ms", "2m59.56s" or "1h2m", as providers write them; null
    /// for anything else.
    /// </summary>
    public static TimeSpan? Duration(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds >= 0 ? TimeSpan.FromSeconds(seconds) : null;
        }

        var parts = Part().Matches(text);
        if (parts.Count == 0 || parts.Sum(p => p.Length) != text.Length)
        {
            return null;
        }

        var total = TimeSpan.Zero;
        foreach (Match part in parts)
        {
            var amount = double.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture);
            total += part.Groups[2].Value switch
            {
                "h" => TimeSpan.FromHours(amount),
                "m" => TimeSpan.FromMinutes(amount),
                "ms" => TimeSpan.FromMilliseconds(amount),
                _ => TimeSpan.FromSeconds(amount),
            };
        }

        return total;
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)(ms|h|m|s)")]
    private static partial Regex Part();
}
