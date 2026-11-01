namespace Naad.Updates;

/// <summary>
/// Decides whether to offer an update. Pure. A release is offered by the daily check only once it
/// has been seen for three days, so a broken release can be pulled before most copies take it, and
/// never when the user skipped that version; Check for updates offers it at once.
/// </summary>
internal static class UpdatePolicy
{
    // Long enough for a bad release to be noticed and replaced, short enough to stay current.
    public static readonly TimeSpan Wait = TimeSpan.FromDays(3);

    /// <summary>The state after a check found <paramref name="version"/> at <paramref name="now"/>.</summary>
    public static UpdateState Saw(UpdateState state, string version, DateTimeOffset now) =>
        state.Seen == version ? state : state with { Seen = version, FirstSeen = now };

    /// <summary>Whether to offer <paramref name="version"/>, given what is remembered.</summary>
    /// <param name="state">After <see cref="Saw"/>.</param>
    /// <param name="version">The newest release.</param>
    /// <param name="now">The time.</param>
    /// <param name="manual">The user chose Check for updates.</param>
    public static bool Offer(UpdateState state, string version, DateTimeOffset now, bool manual) =>
        manual || (state.Skipped != version && now - state.FirstSeen >= Wait);
}
