using Naad.Audio;
using Naad.Meetings;
using Naad.Tests.Dictation;
using Naad.Tests.Live;
using Naad.Transcription;

namespace Naad.Tests.Meetings;

// The recorder with a fake capture and a store in a temporary folder.
[Collection("Log")]
public sealed class MeetingRecorderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"naad-meetings-{Guid.NewGuid():N}");
    private readonly MeetingStore _store;
    private readonly List<string> _copied = [];
    private readonly FakeCapture _capture = new();
    private bool _idle = true;
    private ITranscriber? _transcriber = new FakeTranscriber
    {
        Respond = _ => Task.FromResult(new TranscriptionResult("synthetic words")),
    };

    public MeetingRecorderTests() => _store = new(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task StopAsync_AfterSpeech_SavesEncryptedAndCopies()
    {
        var recorder = Recorder();
        Assert.Equal(MeetingOutcome.Started, await recorder.StartAsync());

        _capture.Send(Speaker.You, SyntheticAudio.Loud(2));
        _capture.Send(Speaker.Others, SyntheticAudio.Loud(2));
        var outcome = await recorder.StopAsync();

        Assert.Equal(MeetingOutcome.Copied, outcome);
        Assert.True(_capture.Stopped && _capture.Disposed);
        Assert.Equal(["You: synthetic words" + Environment.NewLine + "Others: synthetic words"], _copied);
        var saved = Assert.Single(_store.All());
        Assert.Equal(2, saved.Segments.Count);
        Assert.StartsWith("Meeting · ", saved.Title);
        Assert.DoesNotContain("synthetic words",
            File.ReadAllText(Directory.GetFiles(_folder, "*.meeting").Single()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_NothingSaid_SavesNothing()
    {
        var recorder = Recorder();
        await recorder.StartAsync();

        _capture.Send(Speaker.You, SyntheticAudio.Quiet(3));

        Assert.Equal(MeetingOutcome.NothingHeard, await recorder.StopAsync());
        Assert.Empty(_store.All());
        Assert.Empty(_copied);
    }

    [Fact]
    public async Task StartAsync_DuringADictation_IsRefused()
    {
        _idle = false;
        var recorder = Recorder();

        Assert.Equal(MeetingOutcome.DictationRunning, await recorder.StartAsync());
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task StartAsync_NoTranscriber_IsRefused()
    {
        _transcriber = null;

        Assert.Equal(MeetingOutcome.NoTranscriber, await Recorder().StartAsync());
    }

    [Fact]
    public async Task StartAsync_MicrophoneFails_SaysSo()
    {
        _capture.Failure = new(MicrophoneError.Blocked);

        Assert.Equal(MeetingOutcome.CaptureFailed, await Recorder().StartAsync());
        Assert.True(_capture.Disposed);
    }

    [Fact]
    public async Task Delete_RemovesTheMeeting()
    {
        var recorder = Recorder();
        await recorder.StartAsync();
        _capture.Send(Speaker.You, SyntheticAudio.Loud(2));
        await recorder.StopAsync();

        _store.Delete(_store.All().Single().Id);

        Assert.Empty(_store.All());
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task StartAsync_WhileTheLastMeetingFinishes_StartsNothingAndKeepsIt()
    {
        var reply = new TaskCompletionSource<TranscriptionResult>();
        _transcriber = new FakeTranscriber { Respond = _ => reply.Task };
        var recorder = Recorder();
        await recorder.StartAsync();
        _capture.Send(Speaker.You, SyntheticAudio.Loud(2));

        var stop = recorder.StopAsync();
        Assert.True(recorder.IsBusy && !recorder.IsRecording);
        Assert.Equal(MeetingOutcome.Unchanged, await recorder.StartAsync());
        Assert.Same(stop, recorder.StopAsync());
        reply.SetResult(new("synthetic words"));

        Assert.Equal(MeetingOutcome.Copied, await stop);
        Assert.False(recorder.IsBusy);
        Assert.Single(_store.All());
    }

    [Fact]
    public async Task ExitAsync_WhileRecording_SavesBeforeQuitting()
    {
        var recorder = Recorder();
        var actions = new MeetingActions(recorder, _store, _ => Task.FromResult(true), () => null);
        await recorder.StartAsync();
        _capture.Send(Speaker.You, SyntheticAudio.Loud(2));
        var exits = 0;

        var first = actions.ExitAsync(() => exits++);
        await actions.ExitAsync(() => exits++);
        await first;

        Assert.Equal(1, exits);
        Assert.Single(_store.All());
    }

    [Fact]
    public async Task StartAsync_CaptureThrows_DisposesItAndSaysSo()
    {
        _capture.Throws = new InvalidOperationException("synthetic");

        Assert.Equal(MeetingOutcome.CaptureFailed, await Recorder().StartAsync());
        Assert.True(_capture.Disposed);
    }

    [Fact]
    public async Task SummarizeAsync_RenamedMeanwhile_KeepsBothTheTitleAndTheSummary()
    {
        var (actions, meeting, reply) = await SavedMeetingWithPendingSummary();

        var summarizing = actions.SummarizeAsync(meeting, CancellationToken.None);
        actions.Rename(meeting, "Synthetic new title");
        reply.SetResult(new("## Summary\n- A synthetic point."));
        await summarizing;

        var saved = Assert.Single(_store.All());
        Assert.Equal("Synthetic new title", saved.Title);
        Assert.Equal("## Summary\n- A synthetic point.", saved.Summary);
    }

    [Fact]
    public async Task SummarizeAsync_DeletedMeanwhile_StaysDeleted()
    {
        var (actions, meeting, reply) = await SavedMeetingWithPendingSummary();

        var summarizing = actions.SummarizeAsync(meeting, CancellationToken.None);
        actions.Delete(meeting);
        reply.SetResult(new("## Summary\n- A synthetic point."));

        Assert.Null(await summarizing);
        Assert.Empty(_store.All());
        Assert.Empty(Directory.GetFiles(_folder));
    }

    // A recorded meeting, and actions whose language model answers only when told to.
    private async Task<(MeetingActions, MeetingRecord, TaskCompletionSource<Naad.LanguageModel.CompletionResult>)>
        SavedMeetingWithPendingSummary()
    {
        var recorder = Recorder();
        await recorder.StartAsync();
        _capture.Send(Speaker.You, SyntheticAudio.Loud(2));
        await recorder.StopAsync();
        var reply = new TaskCompletionSource<Naad.LanguageModel.CompletionResult>();
        var actions = new MeetingActions(recorder, _store, _ => Task.FromResult(true), () => new PendingModel(reply));
        return (actions, _store.All().Single(), reply);
    }

    private MeetingRecorder Recorder() => new(
        _store,
        () => _transcriber,
        () => _idle,
        () => -1,
        () => false,
        text =>
        {
            _copied.Add(text);
            return Task.FromResult(true);
        },
        (_, captured, _) =>
        {
            _capture.Captured = captured;
            return Task.FromResult<IMeetingCapture>(_capture);
        });

    private sealed class PendingModel(TaskCompletionSource<Naad.LanguageModel.CompletionResult> reply)
        : Naad.LanguageModel.ILanguageModel
    {
        public Task<Naad.LanguageModel.CompletionResult> CompleteAsync(
            Naad.LanguageModel.ChatPrompt prompt, CancellationToken cancellationToken) => reply.Task;
    }

    private sealed class FakeCapture : IMeetingCapture
    {
        public Action<Speaker, byte[]>? Captured { get; set; }

        public MicrophoneFailure? Failure { get; set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public Exception? Throws { get; set; }

        public MicrophoneFailure? Start() => Throws is null ? Failure : throw Throws;

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        public void Send(Speaker speaker, byte[] pcm) => Captured!(speaker, pcm);
    }
}
