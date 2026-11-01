using Naad.History;

namespace Naad.Tests.History;

public sealed class HistoryRecorderTests
{
    [Fact]
    public void RecentTexts_Records_NewestFirstWithoutFailures()
    {
        HistoryRecord[] records =
        [
            Record("synthetic newest", HistoryOutcome.Kept),
            Record("", HistoryOutcome.Failed),
            Record("synthetic older", HistoryOutcome.Pasted),
        ];

        Assert.Equal(["synthetic newest", "synthetic older"], HistoryRecorder.RecentTexts(records, 10));
    }

    [Fact]
    public void RecentTexts_MoreThanAsked_TakesTheNewest() =>
        Assert.Equal(
            ["synthetic a"],
            HistoryRecorder.RecentTexts(
                [Record("synthetic a", HistoryOutcome.Pasted), Record("synthetic b", HistoryOutcome.Pasted)], 1));

    private static HistoryRecord Record(string text, HistoryOutcome outcome) =>
        new(Guid.NewGuid(), DateTimeOffset.UnixEpoch, null, 1000, text, text, 10, 0, outcome, HasAudio: false, null);
}
