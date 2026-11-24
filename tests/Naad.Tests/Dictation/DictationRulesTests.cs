using Naad.Dictation;
using Naad.Insertion;

namespace Naad.Tests.Dictation;

public sealed class DictationRulesTests
{
    [Fact]
    public void ForFailure_EveryOutcome_MapsWithoutThrowing()
    {
        foreach (var outcome in Enum.GetValues<InsertionOutcome>())
        {
            _ = InsertionOutcomes.ForFailure(outcome);
        }
    }

    [Fact]
    public void ForFailure_Busy_KeepsTheTranscript()
    {
        Assert.Equal(TranscriptKeptReason.NotInserted, InsertionOutcomes.ForFailure(InsertionOutcome.Busy)?.Reason);
    }

    [Theory]
    [InlineData(DictationState.Idle, false)]
    [InlineData(DictationState.Recording, true)]
    [InlineData(DictationState.Transcribing, true)]
    [InlineData(DictationState.Inserting, false)]
    internal void EscapeCancels_OnlyWhileItCanStopTheDictation(DictationState state, bool expected)
    {
        Assert.Equal(expected, DictationController.EscapeCancels(state));
    }
}
