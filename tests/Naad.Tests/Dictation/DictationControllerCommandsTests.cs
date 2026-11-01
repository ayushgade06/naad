using Naad.Cleanup;
using Naad.Context;
using Naad.Dictation;
using Naad.History;

namespace Naad.Tests.Dictation;

// Esc, the latch, Paste again, the history report and Edit Mode, through the real controller.
[Collection("Log")]
public sealed class DictationControllerCommandsTests : IDisposable
{
    private static readonly TimeSpan LongHold = TimeSpan.FromSeconds(1);
    private readonly Harness _h = new();
    private readonly List<DictationReport> _reports = [];

    public DictationControllerCommandsTests() => _h.Controller.Finished += _reports.Add;

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Escape_WhileRecording_DiscardsWithoutTranscribing()
    {
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(0, _h.Transcriber.Calls);
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task Latch_ThenRelease_KeepsRecordingUntilTheNextPress()
    {
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(_h.Controller.Latch);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);

        Assert.Equal(DictationState.Recording, _h.Controller.State);

        await _h.Ui.RunAsync(_h.Controller.Press);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_Pasted_ReportsItForTheHistory()
    {
        await _h.DictateAsync(LongHold);

        var report = Assert.Single(_reports);
        Assert.Equal((Harness.Transcript, Harness.Transcript, HistoryOutcome.Pasted), (report.Raw, report.Text,
            report.Outcome));
        Assert.NotNull(report.Wav);
        Assert.Equal(1_000, report.AudioMilliseconds);
    }

    [Fact]
    public async Task PasteAgain_AfterADictation_PastesTheSameText()
    {
        await _h.DictateAsync(LongHold);

        Assert.True(await _h.Ui.RunAsync(_h.Controller.PasteAgainAsync));
        Assert.Equal(2, _h.Pastes);
    }

    [Fact]
    public async Task PasteAgain_NothingDictated_DoesNothing()
    {
        Assert.False(await _h.Ui.RunAsync(_h.Controller.PasteAgainAsync));
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task Edit_Fails_PastesNothingAndSaysSo()
    {
        var failures = new List<bool>();
        _h.Controller.EditFailed += failures.Add;
        _h.Controller.UseContextReader(_ => Task.FromResult(new DictationContext("app", null, "selected", null)));
        _h.Controller.UseCleanup(new FakeCleanup(new("", CleanupOutcome.EditFailed)));

        await _h.DictateAsync(LongHold);

        Assert.Equal([false], failures);
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task Edit_ManualWithoutSelection_AsksForOne()
    {
        var failures = new List<bool>();
        _h.Controller.EditFailed += failures.Add;
        _h.Controller.UseContextReader(_ => Task.FromResult(DictationContext.None with { NeedsSelection = true }));

        await _h.DictateAsync(LongHold);

        Assert.Equal([true], failures);
        Assert.Equal(0, _h.Pastes);
    }
}
