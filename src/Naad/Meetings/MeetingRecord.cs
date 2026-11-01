using System.Globalization;

namespace Naad.Meetings;

/// <summary>
/// One recorded meeting. User content: stored encrypted with DPAPI, shown in Naad's window,
/// never logged.
/// </summary>
/// <param name="Id">Names its files.</param>
/// <param name="Title">Shown in the list; the user can rename it.</param>
/// <param name="Started">When recording started.</param>
/// <param name="Duration">How long it ran.</param>
/// <param name="Segments">What was said, in order.</param>
internal sealed record MeetingRecord(
    Guid Id,
    string Title,
    DateTimeOffset Started,
    TimeSpan Duration,
    IReadOnlyList<MeetingSegment> Segments)
{
    /// <summary>The default title, such as "Meeting · Oct 21, 3:04pm".</summary>
    public static string DefaultTitle(DateTime local) =>
        "Meeting · " + local.ToString("MMM d, h:mm", CultureInfo.InvariantCulture)
        + local.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant();

    public override string ToString() => $"MeetingRecord {{ Id = {Id}, Segments = {Segments.Count} }}";
}
