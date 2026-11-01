using Naad.Cleanup;
using Naad.Context;
using Naad.Dictation;
using Naad.History;
using Naad.Transcription;

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
    public async Task PasteAgain_EscapeDuringCleanup_PastesThisDictationAsSaid()
    {
        await _h.DictateAsync(LongHold);
        _h.Transcriber.Respond = _ => Task.FromResult(new TranscriptionResult("synthetic second"));
        var cleanup = new WaitingCleanup();
        _h.Controller.UseCleanup(cleanup);
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await cleanup.Started.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.True(await _h.Ui.RunAsync(_h.Controller.PasteAgainAsync));
        Assert.Equal("synthetic second", _h.Clipboard.Texts[^1]);
    }

    [Fact]
    public async Task PasteAgain_AfterAFailedEdit_PastesWhatWasSaid()
    {
        await _h.DictateAsync(LongHold);
        _h.Transcriber.Respond = _ => Task.FromResult(new TranscriptionResult("synthetic instruction"));
        _h.Controller.UseContextReader(_ => Task.FromResult(new DictationContext("app", null, "selected", null)));
        _h.Controller.UseCleanup(new FakeCleanup(new("", CleanupOutcome.EditFailed)));
        await _h.DictateAsync(LongHold);
        _h.Controller.UseContextReader(null);

        Assert.True(await _h.Ui.RunAsync(_h.Controller.PasteAgainAsync));
        Assert.Equal("synthetic instruction", _h.Clipboard.Texts[^1]);
    }

    [Fact]
    public async Task Escape_WhileRecording_SaysCancelled()
    {
        var cancelled = 0;
        _h.Controller.Cancelled += () => cancelled++;
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public async Task Escape_WhileTranscribing_SaysCancelled()
    {
        var cancelled = 0;
        _h.Controller.Cancelled += () => cancelled++;
        var cleanup = new WaitingCleanup();
        _h.Controller.UseCleanup(cleanup);
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await cleanup.Started.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, cancelled);
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task PasteAgain_OnlyPressEnterSaid_KeepsThePreviousText()
    {
        await _h.DictateAsync(LongHold);
        _h.Transcriber.Respond = _ => Task.FromResult(new TranscriptionResult("press enter"));
        _h.Controller.UseCleanup(new FakeCleanup(new("", CleanupOutcome.Cleaned, PressEnter: true)));
        await _h.DictateAsync(LongHold);

        Assert.True(await _h.Ui.RunAsync(_h.Controller.PasteAgainAsync));
        Assert.Equal(Harness.Transcript, _h.Clipboard.Texts[^1]);
    }

    [Fact]
    public async Task Escape_Idle_SaysNothing()
    {
        var cancelled = 0;
        _h.Controller.Cancelled += () => cancelled++;

        await _h.Ui.RunAsync(_h.Controller.Escape);

        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task Dictate_ContextReadThrows_StillPastes()
    {
        _h.Controller.UseContextReader(_ => Task.FromException<DictationContext>(new FormatException()));

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Pastes);
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

    // A cleanup that answers only when it is cancelled, as a slow model would be by Esc.
    private sealed class WaitingCleanup : ITranscriptCleanup
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool UsesModel => true;

        public void Prepare()
        {
        }

        public async Task<CleanupResult> CleanAsync(
            string transcript, DictationContext context, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(transcript, CleanupOutcome.Cleaned);
        }
    }
}
