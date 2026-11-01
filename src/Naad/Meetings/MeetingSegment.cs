namespace Naad.Meetings;

/// <summary>One stretch of speech. User content: stored encrypted, never logged.</summary>
/// <param name="Speaker">The track it was heard on.</param>
/// <param name="Start">Seconds from the start of the meeting.</param>
/// <param name="Text">What was said, or the failed-piece marker.</param>
internal sealed record MeetingSegment(Speaker Speaker, double Start, string Text)
{
    /// <summary>Stands in for a piece that couldn't be transcribed, so the gap stays visible.</summary>
    public const string Failed = "[couldn't transcribe]";

    public override string ToString() => $"MeetingSegment {{ Speaker = {Speaker}, Chars = {Text.Length} }}";
}
