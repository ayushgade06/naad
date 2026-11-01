using Naad.Meetings;

namespace Naad.Tests.Meetings;

// Snapshots shaped like Windows' consent store: each key with its LastUsedTimeStart and
// LastUsedTimeStop FILETIMEs (0 for never). Synthetic paths and times only.
public sealed class MeetingDetectorTests
{
    private const string Zoom = @"C:#Users#Someone#AppData#Roaming#Zoom#bin#Zoom.exe";
    private const string Chrome = @"C:#Program Files#Google#Chrome#Application#chrome.exe";
    private const string Recorder = @"C:#Tools#voice-recorder.exe";
    private const string Teams = "MSTeams_8wekyb3d8bbwe";

    private static readonly IReadOnlyCollection<string> NoneIgnored = [];
    private readonly MeetingDetector _detector = new();

    [Fact]
    public void Next_CallAppHeldPastSettle_OffersItOnce()
    {
        var call = Snapshot((Zoom, false, 200, 100));

        Assert.Null(Next(call, 0));
        Assert.Null(Next(call, 2));
        Assert.Equal("zoom", Next(call, 4));
        Assert.Null(Next(call, 6));
        Assert.Null(Next(call, 60));
    }

    [Fact]
    public void Next_AfterTheAppLetGo_OffersTheNextCall()
    {
        var call = Snapshot((Teams, true, 200, 0));
        var ended = Snapshot((Teams, true, 200, 300));

        Assert.Equal("msteams", Next(call, 0, 4));
        Assert.Null(Next(ended, 10));
        Assert.Null(Next(call, 20));
        Assert.Equal("msteams", Next(call, 24));
    }

    [Fact]
    public void Next_OtherAppsAndIgnoredApps_AreNeverOffered()
    {
        var uses = Snapshot((Recorder, false, 200, 0), (Chrome, false, 200, 0));

        Assert.Null(_detector.Next(uses, TimeSpan.Zero, busy: false, ["Chrome.exe"]));
        Assert.Null(_detector.Next(uses, TimeSpan.FromSeconds(10), busy: false, ["Chrome.exe"]));
    }

    [Fact]
    public void Next_WhileBusy_TheCallCountsAsHandled()
    {
        var call = Snapshot((Zoom, false, 200, 0));

        Assert.Null(_detector.Next(call, TimeSpan.Zero, busy: false, NoneIgnored));
        Assert.Null(_detector.Next(call, TimeSpan.FromSeconds(5), busy: true, NoneIgnored));
        Assert.Null(Next(call, 10));
    }

    // Without the app's 20-second pause after an offer, which makes these one offer per call.
    [Fact]
    public void Next_TwoCallApps_OffersOneAtATimeInNameOrder()
    {
        var both = Snapshot((Zoom, false, 200, 0), (Chrome, false, 200, 0));

        Next(both, 0);
        Assert.Equal("chrome", Next(both, 4));
        Assert.Equal("zoom", Next(both, 6));
    }

    // One snapshot, then the detector's answer at each of the given seconds; the last answer.
    private string? Next(IReadOnlyList<MicrophoneUse> uses, params double[] seconds)
    {
        string? last = null;
        foreach (var second in seconds)
        {
            last = _detector.Next(uses, TimeSpan.FromSeconds(second), busy: false, NoneIgnored);
        }

        return last;
    }

    private static IReadOnlyList<MicrophoneUse> Snapshot(
        params (string Key, bool Packaged, long Start, long Stop)[] keys) =>
        [.. keys.Select(k => new MicrophoneUse(
            MicrophoneUsage.AppName(k.Key, k.Packaged), MicrophoneUsage.InUse(k.Start, k.Stop)))];
}
