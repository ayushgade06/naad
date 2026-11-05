using Naad.Cleanup;
using Naad.History;
using Naad.Window;

namespace Naad.Tests.Window;

public sealed class HistoryDetailsTests
{
    [Theory]
    [InlineData(nameof(HistoryOutcome.Pasted), true)]
    [InlineData(nameof(HistoryOutcome.Kept), true)]
    [InlineData(nameof(HistoryOutcome.Failed), false)]
    [InlineData(nameof(HistoryOutcome.Empty), false)]
    public void HasText_Outcome_OnlyWhatWentInOrWasKept(string outcome, bool expected)
    {
        Assert.Equal(expected, HistoryDetails.HasText(Record(Enum.Parse<HistoryOutcome>(outcome))));
    }

    [Fact]
    public void Placeholder_FailedEdit_SaysTheEditFailed()
    {
        Assert.StartsWith("The edit failed", HistoryDetails.Placeholder(Record(HistoryOutcome.Failed, "selected")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholder_Pasted_IsNull()
    {
        Assert.Null(HistoryDetails.Placeholder(Record(HistoryOutcome.Pasted)));
    }

    [Theory]
    [InlineData(nameof(CleanupOutcome.Cleaned), "synthetic", nameof(HistoryOutcome.Kept))]
    [InlineData(nameof(CleanupOutcome.Empty), "", nameof(HistoryOutcome.Empty))]
    [InlineData(nameof(CleanupOutcome.EditFailed), "", nameof(HistoryOutcome.Failed))]
    [InlineData(nameof(CleanupOutcome.Edited), "synthetic", nameof(HistoryOutcome.Kept))]
    public void RetryOutcome_Cleanup_IsKeptEmptyOrFailed(string cleanup, string text, string expected)
    {
        var cleaned = new CleanupResult(text, Enum.Parse<CleanupOutcome>(cleanup));

        Assert.Equal(Enum.Parse<HistoryOutcome>(expected), HistoryDetails.RetryOutcome(cleaned));
    }

    [Theory]
    [InlineData(nameof(HistoryOutcome.Pasted), nameof(HistoryOutcome.Empty), false)]
    [InlineData(nameof(HistoryOutcome.Kept), nameof(HistoryOutcome.Failed), false)]
    [InlineData(nameof(HistoryOutcome.Pasted), nameof(HistoryOutcome.Kept), true)]
    [InlineData(nameof(HistoryOutcome.Empty), nameof(HistoryOutcome.Empty), true)]
    [InlineData(nameof(HistoryOutcome.Failed), nameof(HistoryOutcome.Kept), true)]
    public void RetryReplaces_Outcomes_NeverLosesText(string before, string retried, bool expected)
    {
        var record = Record(Enum.Parse<HistoryOutcome>(before));

        Assert.Equal(expected, HistoryDetails.RetryReplaces(record, Enum.Parse<HistoryOutcome>(retried)));
    }

    [Fact]
    public void Cleanup_EveryOutcome_HasWords()
    {
        foreach (var outcome in Enum.GetValues<CleanupOutcome>())
        {
            Assert.NotEqual(outcome.ToString(), HistoryDetails.Cleanup(outcome));
        }
    }

    private static HistoryRecord Record(HistoryOutcome outcome, string? selection = null) =>
        new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, 1000, "synthetic", "synthetic", 10, 0, outcome,
            HasAudio: true, selection);
}
