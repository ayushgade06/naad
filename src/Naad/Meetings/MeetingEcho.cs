namespace Naad.Meetings;

/// <summary>
/// Leaves out the other side's voice that leaked from the speakers into the microphone: a line
/// under You whose words are mostly in an Others line said at the same time is that echo, not the
/// user. Without echo cancellation (laptop speakers, many USB microphones) every line of theirs
/// would otherwise appear twice. Pure; works on transcribed text only.
/// </summary>
// ponytail: matching text, not audio. The user repeating the other side's words at the same moment
// is left out too, and an echo Whisper heard too differently stays; detect the echo in the audio
// (cross-correlating the two tracks) if either shows up in real calls.
internal static class MeetingEcho
{
    // Echo reaches the microphone within milliseconds, but each track is split into pieces at its
    // own pauses, so the same words may start a piece a few seconds apart.
    private const double WindowSeconds = 5;

    // An echo comes back through a laptop microphone with some words misheard; the user's own
    // reply shares far fewer with what was just said.
    private const double SharedWords = 0.6;

    // "Yes" or "okay" said by both sides is a real reply as often as an echo; short lines stay.
    private const int MinWords = 3;

    /// <summary><paramref name="lines"/>, in time order, without the echoed You lines.</summary>
    public static IReadOnlyList<MeetingSegment> Without(IReadOnlyList<MeetingSegment> lines)
    {
        var others = lines.Where(l => l.Speaker == Speaker.Others && l.Text != MeetingSegment.Failed).ToList();
        if (others.Count == 0)
        {
            return lines;
        }

        return [.. lines.Where(l => !IsEcho(l, others))];
    }

    private static bool IsEcho(MeetingSegment line, List<MeetingSegment> others)
    {
        if (line.Speaker != Speaker.You || line.Text == MeetingSegment.Failed)
        {
            return false;
        }

        var words = Words(line.Text);
        if (words.Length < MinWords)
        {
            return false;
        }

        var heard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in others)
        {
            if (Math.Abs(other.Start - line.Start) <= WindowSeconds)
            {
                heard.UnionWith(Words(other.Text));
            }
        }

        return heard.Count > 0 && words.Count(heard.Contains) >= SharedWords * words.Length;
    }

    private static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(Punctuation))
            .Where(w => w.Length > 0)
            .ToArray();

    private static readonly char[] Punctuation = ['.', ',', '!', '?', ';', ':', '"', '\'', '(', ')', '-', '…'];
}
