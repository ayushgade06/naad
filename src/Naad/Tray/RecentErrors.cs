namespace Naad.Tray;

/// <summary>A problem the tray told the user about: a fixed title, never user content.</summary>
/// <param name="Title">The notification's title, such as "Naad couldn't transcribe".</param>
/// <param name="Time">When it was shown.</param>
internal sealed record RecentError(string Title, DateTimeOffset Time);

/// <summary>The last few problems, for the tray menu. Pure.</summary>
internal static class RecentErrors
{
    /// <summary>Enough to see what has been going wrong without crowding the menu.</summary>
    public const int Kept = 3;

    /// <summary>
    /// <paramref name="errors"/> with <paramref name="added"/> first, newest first, at most
    /// <see cref="Kept"/>. The same problem again straight away is one line, with its newer time.
    /// </summary>
    public static IReadOnlyList<RecentError> Add(IReadOnlyList<RecentError> errors, RecentError added) =>
        [added, .. errors.Skip(errors.Count > 0 && errors[0].Title == added.Title ? 1 : 0).Take(Kept - 1)];
}
