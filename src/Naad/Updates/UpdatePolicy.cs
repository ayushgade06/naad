namespace Naad.Updates;

/// <summary>
/// Decides whether to offer an update. Pure. The automatic check offers a release only three days
/// after it was published, so a broken release can be pulled before most copies take it, and never
/// one the user skipped. Check for updates offers it at once, with a warning while it is that new.
/// </summary>
internal static class UpdatePolicy
{
    // Long enough for a bad release to be noticed and replaced, short enough to stay current.
    public static readonly TimeSpan Wait = TimeSpan.FromDays(3);

    /// <summary>Whether to offer <paramref name="version"/>, published at <paramref name="published"/>.</summary>
    /// <param name="state">What is remembered.</param>
    /// <param name="version">The newest release.</param>
    /// <param name="published">When it was published, from its signed release list.</param>
    /// <param name="now">The time.</param>
    /// <param name="manual">The user chose Check for updates.</param>
    public static bool Offer(
        UpdateState state, string version, DateTimeOffset published, DateTimeOffset now, bool manual) =>
        manual || (state.Skipped != version && now - published >= Wait);

    /// <summary>
    /// Whether a release published at <paramref name="published"/> is still in its first three days,
    /// so Check for updates warns before downloading it. A publish time ahead of this PC's clock
    /// counts as new.
    /// </summary>
    public static bool IsRecent(DateTimeOffset published, DateTimeOffset now) => now - published < Wait;

    /// <summary>How long ago a release came out, such as "5 hours ago".</summary>
    public static string Age(TimeSpan age)
    {
        static string Count(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
        return age.TotalHours < 1 ? "less than an hour ago"
            : age.TotalDays < 1 ? Count((int)age.TotalHours, "hour")
            : Count((int)age.TotalDays, "day");
    }

    /// <summary>
    /// Whether the overlay should say "Update available" after a dictation: once per offered
    /// version, never for a skipped one, so it is a reminder and not a nag.
    /// </summary>
    /// <param name="state">What is remembered.</param>
    /// <param name="offered">The version on offer now, or null.</param>
    public static bool Mark(UpdateState state, string? offered) =>
        offered is not null && state.Marked != offered && state.Skipped != offered;
}
