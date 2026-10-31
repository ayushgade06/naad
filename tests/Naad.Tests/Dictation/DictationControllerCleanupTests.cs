using Naad.Cleanup;
using Naad.Dictation;

namespace Naad.Tests.Dictation;

// The cleanup step between transcription and pasting, through the real controller.
[Collection("Log")]
public sealed class DictationControllerCleanupTests : IDisposable
{
    private static readonly TimeSpan LongHold = TimeSpan.FromSeconds(1);
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Dictate_WithCleanup_PastesTheCleanedText()
    {
        var cleanup = new FakeCleanup(new("Synthetic transcript.", CleanupOutcome.Cleaned));
        _h.Controller.UseCleanup(cleanup);

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, cleanup.Prepared);
        Assert.Equal([Harness.Transcript], cleanup.Cleaned);
        Assert.Equal(1, _h.Cleanups);
        Assert.Equal(0, _h.CleanupFallbacks);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_CleanupFellBack_PastesTheTranscriptAndSaysSo()
    {
        _h.Controller.UseCleanup(new FakeCleanup(new(Harness.Transcript, CleanupOutcome.FellBack)));

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.CleanupFallbacks);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_CleanupSaysEmpty_PastesNothing()
    {
        _h.Controller.UseCleanup(new FakeCleanup(new("", CleanupOutcome.Empty)));

        await _h.DictateAsync(LongHold);

        Assert.Equal(0, _h.Pastes);
        Assert.Equal([DictationState.Recording, DictationState.Transcribing, DictationState.Idle], _h.States);
    }

    [Fact]
    public async Task Dictate_EmptyTranscript_NeverCleans()
    {
        var cleanup = new FakeCleanup(new("invented", CleanupOutcome.Cleaned));
        _h.Controller.UseCleanup(cleanup);
        _h.Transcriber.Respond = _ => Task.FromResult(new Naad.Transcription.TranscriptionResult(""));

        await _h.DictateAsync(LongHold);

        Assert.Empty(cleanup.Cleaned);
        Assert.Equal(0, _h.Pastes);
    }
}
