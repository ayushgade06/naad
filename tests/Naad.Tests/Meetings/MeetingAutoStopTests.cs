using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MeetingAutoStopTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public void Update_CallAppLetsGo_StopsAfterTheGrace()
    {
        var policy = new MeetingAutoStop();

        Assert.Null(policy.Update(Minute, callAppUsesMicrophone: true, Minute));
        Assert.Null(policy.Update(Minute * 2, callAppUsesMicrophone: false, Minute * 2));
        Assert.Null(policy.Update((Minute * 2) + TimeSpan.FromSeconds(20), false, Minute * 2));
        Assert.Equal(AutoStopReason.CallEnded,
            policy.Update((Minute * 2) + MeetingAutoStop.CallEndedGrace, false, Minute * 2));
    }

    [Fact]
    public void Update_CallAppComesBackWithinTheGrace_KeepsRecording()
    {
        var policy = new MeetingAutoStop();
        policy.Update(Minute, true, Minute);
        policy.Update(Minute * 2, false, Minute * 2);

        Assert.Null(policy.Update((Minute * 2) + TimeSpan.FromSeconds(10), true, Minute * 2));
        Assert.Null(policy.Update(Minute * 4, true, Minute * 4));
    }

    [Fact]
    public void Update_NoCallAppEverSeen_NeverStopsForACallEnding()
    {
        // Notes started by hand for a meeting in the room.
        var policy = new MeetingAutoStop();

        Assert.Null(policy.Update(Minute * 30, callAppUsesMicrophone: false, Minute * 29));
    }

    [Fact]
    public void Update_NoOneSpeaks_StopsAfterTheSilence()
    {
        var policy = new MeetingAutoStop();

        Assert.Null(policy.Update(Minute * 9, false, lastSpeech: null));
        Assert.Equal(AutoStopReason.Silence, policy.Update(Minute * 10, false, lastSpeech: null));
        Assert.Null(new MeetingAutoStop().Update(Minute * 15, false, Minute * 6));
        Assert.Equal(AutoStopReason.Silence, new MeetingAutoStop().Update(Minute * 16, false, Minute * 6));
    }

    [Fact]
    public void Update_FourHours_StopsEvenWhileTalking()
    {
        Assert.Equal(AutoStopReason.TimeLimit,
            new MeetingAutoStop().Update(MeetingAutoStop.Longest, true, MeetingAutoStop.Longest));
    }
}
