namespace Naad.Meetings;

/// <summary>
/// Notices a microphone that another app has silenced. A real microphone always picks up some
/// noise, so a run of exact zeros means the audio is blocked, not quiet. Pure: it is given each
/// buffer and the time. One caller at a time.
/// </summary>
internal sealed class MicrophoneWatchdog
{
    // Long enough that a driver's brief zeros at start or after a hiccup don't count.
    public static readonly TimeSpan BlockedAfter = TimeSpan.FromSeconds(2);

    // While still blocked, how often to try again: often enough to recover soon, rarely enough
    // not to churn the device.
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(4);

    private TimeSpan? _zerosSince;
    private TimeSpan _lastRestart;
    private bool _blocked;

    /// <summary>Takes the next buffer, heard at <paramref name="now"/>.</summary>
    public WatchdogVerdict Check(ReadOnlySpan<byte> pcm, TimeSpan now)
    {
        if (pcm.ContainsAnyExcept((byte)0))
        {
            _zerosSince = null;
            if (!_blocked)
            {
                return WatchdogVerdict.Fine;
            }

            _blocked = false;
            return WatchdogVerdict.Recovered;
        }

        _zerosSince ??= now;
        if (now - _zerosSince < BlockedAfter || (_blocked && now - _lastRestart < RetryEvery))
        {
            return WatchdogVerdict.Fine;
        }

        var verdict = _blocked ? WatchdogVerdict.Retry : WatchdogVerdict.Blocked;
        _blocked = true;
        _lastRestart = now;
        return verdict;
    }
}
