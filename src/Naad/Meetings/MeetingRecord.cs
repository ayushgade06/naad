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
/// <param name="Summary">The language model's summary in Markdown, once made; null before.</param>
internal sealed record MeetingRecord(
    Guid Id,
    string Title,
    DateTimeOffset Started,
    TimeSpan Duration,
    IReadOnlyList<MeetingSegment> Segments,
    string? Summary = null)
{
    // Long enough for any real title; the list shows it on one line.
    public const int MaxTitleLength = 120;

    /// <summary>The default title, such as "Meeting · Oct 21, 3:04pm".</summary>
    public static string DefaultTitle(DateTime local) =>
        "Meeting · " + local.ToString("MMM d, h:mm", CultureInfo.InvariantCulture)
        + local.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant();

    /// <summary>
    /// The new title for <paramref name="typed"/>: trimmed and at most 120 characters, or null
    /// when it is blank or the same, so nothing is saved.
    /// </summary>
    public string? Renamed(string? typed)
    {
        var title = (typed ?? "").Trim();
        if (title.Length > MaxTitleLength)
        {
            title = title[..MaxTitleLength].TrimEnd();
        }

        return title.Length == 0 || title == Title ? null : title;
    }

    public override string ToString() => $"MeetingRecord {{ Id = {Id}, Segments = {Segments.Count} }}";
}
