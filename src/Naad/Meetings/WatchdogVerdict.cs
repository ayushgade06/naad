namespace Naad.Meetings;

internal enum WatchdogVerdict
{
    /// <summary>Nothing to do.</summary>
    Fine,

    /// <summary>The microphone just went to exact silence: say so, and reopen it.</summary>
    Blocked,

    /// <summary>Still silent: try reopening it again, without saying so again.</summary>
    Retry,

    /// <summary>Sound is back after a block.</summary>
    Recovered,
}
