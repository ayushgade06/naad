using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MicrophoneWatchdogTests
{
    private static readonly byte[] Zeros = new byte[3200];
    private static readonly byte[] Noise = [.. new byte[3198], 1, 0];
    private readonly MicrophoneWatchdog _watchdog = new();

    [Fact]
    public void Check_QuietRoomNoise_IsFine()
    {
        for (var tenths = 0; tenths < 100; tenths++)
        {
            Assert.Equal(WatchdogVerdict.Fine, _watchdog.Check(Noise, TimeSpan.FromMilliseconds(tenths * 100)));
        }
    }

    [Fact]
    public void Check_TwoSecondsOfExactZeros_BlocksOnceThenRetriesEveryFourSeconds()
    {
        var verdicts = new List<(double, WatchdogVerdict)>();
        for (var tenths = 0; tenths <= 100; tenths++)
        {
            var verdict = _watchdog.Check(Zeros, TimeSpan.FromMilliseconds(tenths * 100));
            if (verdict != WatchdogVerdict.Fine)
            {
                verdicts.Add((tenths / 10.0, verdict));
            }
        }

        Assert.Equal(
            [(2.0, WatchdogVerdict.Blocked), (6.0, WatchdogVerdict.Retry), (10.0, WatchdogVerdict.Retry)],
            verdicts);
    }

    [Fact]
    public void Check_SoundAfterABlock_Recovers()
    {
        _watchdog.Check(Zeros, TimeSpan.Zero);
        _watchdog.Check(Zeros, TimeSpan.FromSeconds(2));

        Assert.Equal(WatchdogVerdict.Recovered, _watchdog.Check(Noise, TimeSpan.FromSeconds(3)));
        Assert.Equal(WatchdogVerdict.Fine, _watchdog.Check(Noise, TimeSpan.FromSeconds(4)));
    }

    [Fact]
    public void Check_ShortRunOfZeros_IsFine()
    {
        _watchdog.Check(Zeros, TimeSpan.Zero);
        _watchdog.Check(Zeros, TimeSpan.FromSeconds(1.5));
        _watchdog.Check(Noise, TimeSpan.FromSeconds(1.6));

        Assert.Equal(WatchdogVerdict.Fine, _watchdog.Check(Zeros, TimeSpan.FromSeconds(3)));
    }
}
