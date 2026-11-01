using Naad.Updates;

namespace Naad.Tests.Updates;

[Collection("Log")]
public sealed class UpdatePolicyTests
{
    private static readonly DateTimeOffset Day0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Offer_NewRelease_WaitsThreeDaysForTheDailyCheck()
    {
        var state = UpdatePolicy.Saw(new(), "2.0.1", Day0);

        Assert.False(UpdatePolicy.Offer(state, "2.0.1", Day0, manual: false));
        Assert.False(UpdatePolicy.Offer(state, "2.0.1", Day0.AddDays(2.9), manual: false));
        Assert.True(UpdatePolicy.Offer(state, "2.0.1", Day0.AddDays(3), manual: false));
    }

    [Fact]
    public void Offer_CheckForUpdates_OffersAtOnce() =>
        Assert.True(UpdatePolicy.Offer(UpdatePolicy.Saw(new(), "2.0.1", Day0), "2.0.1", Day0, manual: true));

    [Fact]
    public void Saw_SameVersionAgain_KeepsWhenItWasFirstSeen()
    {
        var state = UpdatePolicy.Saw(UpdatePolicy.Saw(new(), "2.0.1", Day0), "2.0.1", Day0.AddDays(2));

        Assert.Equal(Day0, state.FirstSeen);
    }

    [Fact]
    public void Saw_NewerVersion_StartsItsOwnWait()
    {
        var state = UpdatePolicy.Saw(UpdatePolicy.Saw(new(), "2.0.1", Day0), "2.0.2", Day0.AddDays(5));

        Assert.False(UpdatePolicy.Offer(state, "2.0.2", Day0.AddDays(5), manual: false));
    }

    [Fact]
    public void Offer_SkippedVersion_IsNeverOfferedByTheDailyCheck()
    {
        var state = UpdatePolicy.Saw(new(Skipped: "2.0.1"), "2.0.1", Day0);

        Assert.False(UpdatePolicy.Offer(state, "2.0.1", Day0.AddDays(30), manual: false));
        Assert.True(UpdatePolicy.Offer(state with { Seen = "2.0.2" }, "2.0.2", Day0.AddDays(30), manual: false));
    }

    [Fact]
    public void Mark_OncePerOfferedVersionAndNeverWhenSkipped()
    {
        Assert.False(UpdatePolicy.Mark(new(), null));
        Assert.True(UpdatePolicy.Mark(new(), "2.0.1"));
        Assert.False(UpdatePolicy.Mark(new(Marked: "2.0.1"), "2.0.1"));
        Assert.True(UpdatePolicy.Mark(new(Marked: "2.0.1"), "2.0.2"));
        Assert.False(UpdatePolicy.Mark(new(Skipped: "2.0.2"), "2.0.2"));
    }

    [Fact]
    public void LoadAndSave_RoundTrip_AndAMissingOrBrokenFileRemembersNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"naad-update-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal(new UpdateState(), UpdateState.Load(path));
            new UpdateState("2.0.1", Day0, "2.0.0", "2.0.1").Save(path);
            Assert.Equal(new UpdateState("2.0.1", Day0, "2.0.0", "2.0.1"), UpdateState.Load(path));
            File.WriteAllText(path, "not json");
            Assert.Equal(new UpdateState(), UpdateState.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
