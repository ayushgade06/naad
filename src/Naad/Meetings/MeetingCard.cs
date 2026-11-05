using System.Globalization;

namespace Naad.Meetings;

/// <summary>What the live meeting card shows at one moment.</summary>
/// <param name="Visible">Whether the card shows at all: only while recording or finishing.</param>
/// <param name="Clock">The time the meeting has run, such as "4:07" or "1:02:30".</param>
/// <param name="Banner">A line above the text: the microphone blocked, or finishing; null for none.</param>
/// <param name="Placeholder">What the lines area says before any line arrives; null once one has.</param>
/// <param name="Finishing">The meeting has stopped and its last pieces are being transcribed.</param>
internal sealed record MeetingCardState(
    bool Visible, string Clock, string? Banner, string? Placeholder, bool Finishing);

/// <summary>The live meeting card's state, from the recorder's numbers. Pure.</summary>
internal static class MeetingCard
{
    public const string Blocked = "Another app is blocking the microphone. Reconnecting...";
    public const string Listening = "Listening. Lines show up here each time someone pauses.";
    public const string Writing = "Finishing: transcribing the last few seconds...";

    /// <param name="recording">The meeting is recording.</param>
    /// <param name="finishing">It has stopped and is still being transcribed.</param>
    /// <param name="blocked">The microphone is silent or away.</param>
    /// <param name="elapsed">How long it has run.</param>
    /// <param name="lines">How many lines have arrived.</param>
    public static MeetingCardState Describe(bool recording, bool finishing, bool blocked, TimeSpan elapsed, int lines)
    {
        var clock = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        finishing &= !recording;
        return new(
            recording || finishing,
            clock,
            finishing ? Writing : blocked && recording ? Blocked : null,
            lines > 0 || finishing ? null : Listening,
            finishing);
    }
}
