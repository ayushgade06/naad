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

    [Fact]
    public async Task Press_ManualEditWithNothingSelected_NeverOpensTheMicrophone()
    {
        var failures = new List<bool>();
        _h.Controller.EditFailed += failures.Add;
        _h.Controller.UseEditIntent(() => EditIntent.EditRequired);
        _h.Controller.UseContextReader(_ => Task.FromResult(DictationContext.None with { NeedsSelection = true }));
        var refused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Controller.EditFailed += _ => refused.TrySetResult();

        await _h.Ui.RunAsync(_h.Controller.Press);
        await refused.Task.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal([true], failures);
        Assert.Equal(0, _h.Recorder.Starts);
        Assert.True(_h.Controller.IsIdle);
    }

    [Fact]
    public async Task Press_ManualEditWithASelection_RecordsAndEdits()
    {
        _h.Controller.UseEditIntent(() => EditIntent.EditRequired);
        var reads = 0;
        _h.Controller.UseContextReader(_ =>
        {
            reads++;
            return Task.FromResult(
                new DictationContext("app", null, "selected", null, Invocation: EditInvocation.Manual));
        });
        var cleanup = new FakeCleanup(new("synthetic rewrite", CleanupOutcome.Edited));
        _h.Controller.UseCleanup(cleanup);
        var recording = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Controller.MicrophoneOpened += () => recording.TrySetResult();

        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        await recording.Task.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, reads);
        Assert.Equal(EditInvocation.Manual, Assert.Single(_reports).Invocation);
    }

    [Fact]
    public async Task Press_ReleasedDuringTheSelectionCheck_StartsNothing()
    {
        var read = new TaskCompletionSource<DictationContext>();
        _h.Controller.UseEditIntent(() => EditIntent.EditRequired);
        _h.Controller.UseContextReader(_ => read.Task);

        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(_h.Controller.Release);
        read.SetResult(new DictationContext("app", null, "selected", null));
        await _h.Ui.RunAsync(() => { });

        Assert.Equal(0, _h.Recorder.Starts);
        Assert.True(_h.Controller.IsIdle);
    }

    [Fact]
    public async Task Dictate_LateSummary_IsStoppedSoCleanupDoesNotWait()
    {
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Controller.UseCleanup(new FakeCleanup(new("synthetic", CleanupOutcome.Cleaned)));
        _h.Controller.UseContextSummary(async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                stopped.TrySetResult();
            }

            return ContextSummary.None;
        });

        await _h.DictateAsync(LongHold);

        await stopped.Task.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_Summary_GoesToCleanup()
    {
        var cleanup = new FakeCleanup(new("synthetic", CleanupOutcome.Cleaned));
        _h.Controller.UseCleanup(cleanup);
        _h.Controller.UseContextSummary((_, _) => Task.FromResult(new ContextSummary("They write an email.")));

        await _h.DictateAsync(LongHold);

        Assert.Equal("They write an email.", Assert.Single(cleanup.Contexts).Summary);
    }

    [Fact]
    public async Task Dictate_SummaryFails_SaysWhyAndStillPastes()
    {
        var failures = new List<ContextFailure>();
        _h.Controller.ContextFailed += failures.Add;
        _h.Controller.UseCleanup(new FakeCleanup(new("synthetic", CleanupOutcome.Cleaned)));
        _h.Controller.UseContextSummary((_, _) => Task.FromResult(new ContextSummary(null, ContextFailure.Key)));

        await _h.DictateAsync(LongHold);

        Assert.Equal([ContextFailure.Key], failures);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_SummaryNeverFinishes_PastesWithoutIt()
    {
        _h.Controller.UseContextSummary((_, token) => Task.Delay(Timeout.Infinite, token)
            .ContinueWith(_ => ContextSummary.None, TaskScheduler.Default));

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Pastes);
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
