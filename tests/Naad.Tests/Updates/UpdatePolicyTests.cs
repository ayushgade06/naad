using Naad.Updates;

namespace Naad.Tests.Updates;

[Collection("Log")]
public sealed class UpdatePolicyTests
{
    private static readonly DateTimeOffset Published = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Offer_NewRelease_WaitsThreeDaysFromPublishingForTheAutomaticCheck()
    {
        Assert.False(UpdatePolicy.Offer(new(), "2.0.1", Published, Published, manual: false));
        Assert.False(UpdatePolicy.Offer(new(), "2.0.1", Published, Published.AddDays(2.9), manual: false));
        Assert.True(UpdatePolicy.Offer(new(), "2.0.1", Published, Published.AddDays(3), manual: false));
    }

    [Fact]
    public void Offer_PublishedAheadOfThisClock_Waits() =>
        Assert.False(UpdatePolicy.Offer(new(), "2.0.1", Published.AddDays(1), Published, manual: false));

    [Fact]
    public void Offer_CheckForUpdates_OffersAtOnceEvenWhenSkipped() =>
        Assert.True(UpdatePolicy.Offer(new(Skipped: "2.0.1"), "2.0.1", Published, Published, manual: true));

    [Fact]
    public void Offer_SkippedVersion_IsNeverOfferedByTheAutomaticCheck()
    {
        var later = Published.AddDays(30);

        Assert.False(UpdatePolicy.Offer(new(Skipped: "2.0.1"), "2.0.1", Published, later, manual: false));
        Assert.True(UpdatePolicy.Offer(new(Skipped: "2.0.1"), "2.0.2", Published, later, manual: false));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(71.9, true)]
    [InlineData(72, false)]
    [InlineData(-5, true)]
    public void IsRecent_HoursSincePublishing_IsTheFirstThreeDays(double hours, bool expected) =>
        Assert.Equal(expected, UpdatePolicy.IsRecent(Published, Published.AddHours(hours)));

    [Theory]
    [InlineData(0.5, "less than an hour ago")]
    [InlineData(1, "1 hour ago")]
    [InlineData(5.9, "5 hours ago")]
    [InlineData(24, "1 day ago")]
    [InlineData(60, "2 days ago")]
    public void Age_Hours_ReadsNaturally(double hours, string expected) =>
        Assert.Equal(expected, UpdatePolicy.Age(TimeSpan.FromHours(hours)));

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
            new UpdateState("2.0.0", "2.0.1", Published).Save(path);
            Assert.Equal(new UpdateState("2.0.0", "2.0.1", Published), UpdateState.Load(path));
            Assert.False(File.Exists(path + ".tmp"));
            File.WriteAllText(path, "not json");
            Assert.Equal(new UpdateState(), UpdateState.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_FileFromBeforeLastChecked_KeepsTheSkippedVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"naad-update-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"Seen":"2.0.1","FirstSeen":"2026-11-01T09:00:00+00:00","Skipped":"2.0.1"}""");

            Assert.Equal(new UpdateState(Skipped: "2.0.1"), UpdateState.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
