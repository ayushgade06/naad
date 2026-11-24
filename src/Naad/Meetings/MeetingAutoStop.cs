namespace Naad.Meetings;

/// <summary>Why meeting notes stopped on their own. The values are logged; they hold no user content.</summary>
internal enum AutoStopReason
{
    /// <summary>The call app let go of the microphone.</summary>
    CallEnded,

    /// <summary>No one spoke for <see cref="MeetingAutoStop.Silence"/>.</summary>
    Silence,

    /// <summary>The PC went to sleep.</summary>
    Sleep,

    /// <summary>The meeting reached <see cref="MeetingAutoStop.Longest"/>.</summary>
    TimeLimit,
}

/// <summary>
/// Decides when meeting notes stop on their own, so a meeting someone forgot to stop doesn't record
/// for hours. The call-ended rule applies only once a call app was seen using the microphone during
/// this meeting, so notes started by hand for an in-person meeting are never cut off by it. Sleep is
/// an event, handled by the caller. Pure; one per meeting, fed every few seconds.
/// </summary>
internal sealed class MeetingAutoStop
{
    /// <summary>Long enough to ride out a call app dropping the microphone for a moment, or rejoining.</summary>
    public static readonly TimeSpan CallEndedGrace = TimeSpan.FromSeconds(30);

    /// <summary>Longer than any real pause in a meeting, short enough not to record an empty room for long.</summary>
    public static readonly TimeSpan Silence = TimeSpan.FromMinutes(10);

    /// <summary>Longer than any meeting someone means to record.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromHours(4);

    private bool _sawCall;
    private TimeSpan? _callGoneSince;

    /// <summary>The reason to stop now, or null to keep recording. The time limit wins, then a finished call.</summary>
    /// <param name="elapsed">How long the meeting has recorded.</param>
    /// <param name="callAppUsesMicrophone">Whether a call app or browser holds the microphone now.</param>
    /// <param name="lastSpeech">When anyone last spoke, from the meeting's start; null before anyone did.</param>
    public AutoStopReason? Update(TimeSpan elapsed, bool callAppUsesMicrophone, TimeSpan? lastSpeech)
    {
        if (callAppUsesMicrophone)
        {
            _sawCall = true;
            _callGoneSince = null;
        }
        else if (_sawCall)
        {
            _callGoneSince ??= elapsed;
        }

        if (elapsed >= Longest)
        {
            return AutoStopReason.TimeLimit;
        }

        if (elapsed - _callGoneSince >= CallEndedGrace)
        {
            return AutoStopReason.CallEnded;
        }

        return elapsed - (lastSpeech ?? TimeSpan.Zero) >= Silence ? AutoStopReason.Silence : null;
    }
}
