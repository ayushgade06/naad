using System.Globalization;
using System.Text;

namespace Naad.Meetings;

/// <summary>How the Meetings page lists, describes, searches and exports meetings. Pure. User content.</summary>
internal static class MeetingList
{
    /// <summary>A time into the meeting as mm:ss, or h:mm:ss from an hour on.</summary>
    public static string Clock(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>"2 speakers · 01:38": who spoke (You, Others or both) and how long it ran.</summary>
    public static string SummaryLine(MeetingRecord meeting)
    {
        var speakers = MeetingTranscript.Turns(meeting.Segments).Select(t => t.Speaker).Distinct().Count();
        return $"{speakers} {(speakers == 1 ? "speaker" : "speakers")} · {Clock(meeting.Duration.TotalSeconds)}";
    }

    /// <summary>"Today", "Yesterday", a day such as "Monday, Oct 20", or with its year if not this one.</summary>
    public static string DayLabel(DateOnly day, DateOnly today) =>
        day == today ? "Today"
        : day == today.AddDays(-1) ? "Yesterday"
        : day.ToString(day.Year == today.Year ? "dddd, MMM d" : "dddd, MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Meetings grouped by the local day they started, newest day and meeting first.</summary>
    public static IReadOnlyList<(string Day, IReadOnlyList<MeetingRecord> Meetings)> ByDay(
        IEnumerable<MeetingRecord> meetings, DateOnly today, TimeZoneInfo zone) =>
        [.. meetings
            .OrderByDescending(m => m.Started)
            .GroupBy(m => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(m.Started, zone).DateTime))
            .Select(g => (DayLabel(g.Key, today), (IReadOnlyList<MeetingRecord>)[.. g]))];

    /// <summary>A turn's text split after each sentence's ending punctuation, for one bubble each.</summary>
    public static IReadOnlyList<string> Sentences(string text) =>
        [.. System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[.!?。！？])\s+")
            .Where(s => s.Length > 0)];

    /// <summary>Whether the title, summary or anything said contains <paramref name="query"/>, ignoring case.</summary>
    public static bool Matches(MeetingRecord meeting, string query) =>
        query.Trim() is not { Length: > 0 } q
        || meeting.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || (meeting.Summary?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
        || meeting.Segments.Any(s => s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>The meeting as Markdown: title, when and how long, the summary if made, and each turn.</summary>
    public static string Markdown(MeetingRecord meeting, TimeZoneInfo zone)
    {
        var started = TimeZoneInfo.ConvertTime(meeting.Started, zone);
        var markdown = new StringBuilder()
            .Append("# ").AppendLine(meeting.Title)
            .AppendLine()
            .Append(started.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" · ").AppendLine(SummaryLine(meeting))
            .AppendLine();
        if (meeting.Summary is { Length: > 0 } summary)
        {
            markdown.AppendLine(summary.Trim()).AppendLine();
        }

        markdown.AppendLine("## Transcript").AppendLine();
        foreach (var turn in MeetingTranscript.Turns(meeting.Segments))
        {
            markdown.Append("**[").Append(Clock(turn.Start)).Append("] ").Append(turn.Speaker).Append(":** ")
                .AppendLine(turn.Text).AppendLine();
        }

        return markdown.ToString().TrimEnd() + Environment.NewLine;
    }
}
