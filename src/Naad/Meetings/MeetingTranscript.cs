namespace Naad.Meetings;

/// <summary>Turns a meeting's segments into readable text. Pure. The text is user content.</summary>
internal static class MeetingTranscript
{
    /// <summary>
    /// The segments in time order (ties keep their order), blanks dropped, and each run of one
    /// speaker joined into one turn that starts when the run does.
    /// </summary>
    public static IReadOnlyList<MeetingSegment> Turns(IEnumerable<MeetingSegment> segments)
    {
        var turns = new List<MeetingSegment>();
        foreach (var segment in segments.Where(s => s.Text.Trim().Length > 0).OrderBy(s => s.Start))
        {
            if (turns.Count > 0 && turns[^1].Speaker == segment.Speaker)
            {
                turns[^1] = turns[^1] with { Text = turns[^1].Text + " " + segment.Text.Trim() };
            }
            else
            {
                turns.Add(segment with { Text = segment.Text.Trim() });
            }
        }

        return turns;
    }

    /// <summary>One line per turn, "You: …" or "Others: …", for the clipboard.</summary>
    public static string ClipboardText(IEnumerable<MeetingSegment> segments) =>
        string.Join(Environment.NewLine, Turns(segments).Select(t => $"{t.Speaker}: {t.Text}"));
}
