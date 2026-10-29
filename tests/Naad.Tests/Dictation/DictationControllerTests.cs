using System.Buffers.Binary;
using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Tests.Insertion;
using Naad.Transcription;

namespace Naad.Tests.Dictation;

// End to end through the real controller, session, silence gate, WAV encoder and inserter, with
// fakes only at the edges: the microphone, the transcription service, the clipboard, input and
// focus. No network, no real clipboard, no keyboard. The controller logs, so it shares the Log
// collection.
[Collection("Log")]
public sealed class DictationControllerTests : IDisposable
{
    private const string Transcript = "synthetic transcript";
    private static readonly TimeSpan LongHold = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Dictate_Normally_PastesOnceRestoresClipboardAndReturnsToIdle()
    {
        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Recorder.Starts);
        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
        Assert.Same(_h.Original, _h.Clipboard.Contents);
        Assert.Equal(
            [DictationState.Recording, DictationState.Transcribing, DictationState.Inserting, DictationState.Idle],
            _h.States);
        Assert.Empty(_h.Kept);
    }

    [Fact]
    public async Task Dictate_Twice_BothArePasted()
    {
        await _h.DictateAsync(LongHold);
        await _h.DictateAsync(LongHold);

        Assert.Equal(2, _h.Recorder.Starts);
        Assert.Equal(2, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_UploadsAValidWav()
    {
        await _h.DictateAsync(LongHold);

        var wav = Assert.Single(_h.Transcriber.Uploads);
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(Wav.HeaderBytes + Harness.Speech.Length, wav.Length);
    }

    [Fact]
    public async Task Dictate_SilentRecording_NeverUploadsAndSaysNothingWasHeard()
    {
        _h.Recorder.Pcm = new byte[32000];

        await _h.DictateAsync(LongHold);

        Assert.Equal(0, _h.Transcriber.Calls);
        Assert.Equal(1, _h.NothingHeard);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Clipboard.Calls);
    }

    [Fact]
    public async Task Dictate_TooShortRecording_NeverUploads()
    {
        _h.Recorder.Pcm = Harness.Speech[..3000];

        await _h.DictateAsync(LongHold);

        Assert.Equal(0, _h.Transcriber.Calls);
        Assert.Equal(0, _h.NothingHeard);
    }

    [Fact]
    public async Task Dictate_ShortHold_DiscardsWithoutUploading()
    {
        await _h.DictateAsync(TimeSpan.FromMilliseconds(200));

        Assert.Equal(0, _h.Transcriber.Calls);
        Assert.Equal([DictationState.Recording, DictationState.Idle], _h.States);
    }

    [Theory]
    [InlineData("OtherKey")]
    [InlineData("Mouse")]
    public async Task Cancel_WhileRecording_DiscardsAndAcceptsTheNextPress(string cause)
    {
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(() => _h.Controller.Cancel(Enum.Parse<CancelCause>(cause)));
        await idle.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_MicrophoneFails_ReportsItAndTheNextDictationWorks()
    {
        _h.Recorder.Failure = new MicrophoneFailure(MicrophoneError.InUse, 4);

        await _h.DictateAsync(LongHold);
        _h.Recorder.Failure = null;
        await _h.DictateAsync(LongHold);

        Assert.Equal([MicrophoneError.InUse], _h.MicrophoneErrors);
        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_RecorderFailsWhileStopping_ReturnsToIdleAndTheNextWorks()
    {
        _h.Recorder.StopError = new InvalidOperationException("device lost");

        await _h.DictateAsync(LongHold);
        _h.Recorder.StopError = null;
        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Theory]
    [InlineData("Unauthorized")]
    [InlineData("RateLimited")]
    [InlineData("ServerError")]
    [InlineData("Network")]
    [InlineData("Timeout")]
    [InlineData("MalformedResponse")]
    public async Task Dictate_TranscriptionFails_ReportsItPastesNothingAndRecovers(string error)
    {
        var failure = Enum.Parse<TranscriptionError>(error);
        _h.Transcriber.Respond = _ => Task.FromResult(new TranscriptionResult("", new(failure)));

        await _h.DictateAsync(LongHold);

        Assert.Equal([failure], _h.TranscriptionErrors);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Clipboard.Calls);
        Assert.Equal(DictationState.Idle, _h.Controller.State);
    }

    [Fact]
    public async Task Dictate_EmptyTranscript_PastesNothing()
    {
        _h.Transcriber.Respond = _ => Task.FromResult(new TranscriptionResult(""));

        await _h.DictateAsync(LongHold);

        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Kept);
        Assert.Equal([DictationState.Recording, DictationState.Transcribing, DictationState.Idle], _h.States);
    }

    [Theory]
    [InlineData("FocusChanged", "WindowChanged")]
    [InlineData("TargetClosed", "WindowChanged")]
    [InlineData("NoTarget", "WindowChanged")]
    [InlineData("TargetElevated", "TargetElevated")]
    public async Task Dictate_TargetCantTakeText_KeepsTranscriptWithoutTouchingTheClipboard(
        string decision, string reason)
    {
        _h.Decision = Enum.Parse<InsertionDecision>(decision);

        await _h.DictateAsync(LongHold);

        Assert.Equal([Enum.Parse<TranscriptKeptReason>(reason)], _h.Kept);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Clipboard.Calls);
    }

    [Fact]
    public async Task CopyKeptTranscript_AfterFocusChange_PutsItOnTheClipboardEachTime()
    {
        _h.Decision = InsertionDecision.FocusChanged;
        await _h.DictateAsync(LongHold);

        Assert.True(await _h.Ui.RunAsync(_h.Controller.CopyKeptTranscriptAsync));
        Assert.True(await _h.Ui.RunAsync(_h.Controller.CopyKeptTranscriptAsync));

        Assert.Equal(Transcript, FakeClipboard.ReadText(_h.Clipboard.Contents));
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task CopyKeptTranscript_NothingKept_ReturnsFalse() =>
        Assert.False(await _h.Ui.RunAsync(_h.Controller.CopyKeptTranscriptAsync));

    [Fact]
    public async Task Dictate_FocusLostDuringModifierWait_KeepsTranscript()
    {
        // Focus is fine at the first check and gone by the check right before Ctrl+V.
        var checks = 0;
        _h.CheckTarget = _ => ++checks == 1 ? InsertionDecision.Insert : InsertionDecision.FocusChanged;

        await _h.DictateAsync(LongHold);

        Assert.Equal([TranscriptKeptReason.WindowChanged], _h.Kept);
        Assert.Equal(0, _h.Pastes);
        Assert.Same(_h.Original, _h.Clipboard.Contents);
    }

    [Fact]
    public async Task Dictate_PasteRejected_KeepsTranscriptAndRestoresClipboard()
    {
        _h.PasteAccepted = false;

        await _h.DictateAsync(LongHold);

        Assert.Equal([TranscriptKeptReason.NotInserted], _h.Kept);
        Assert.Same(_h.Original, _h.Clipboard.Contents);
    }

    [Fact]
    public async Task Dictate_ClipboardBusy_TypesTheTextOnce()
    {
        _h.Clipboard.CaptureFails = true;

        await _h.DictateAsync(LongHold);

        Assert.Equal([Transcript], _h.Typed);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Kept);
    }

    [Fact]
    public async Task Press_WhileTranscribing_IsIgnoredAndTheTextIsPastedOnce()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _h.Transcriber.Respond = async _ =>
        {
            started.SetResult();
            await release.Task;
            return new TranscriptionResult(Transcript);
        };
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await started.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        // Auto-repeat or an impatient second press.
        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(_h.Controller.Release);
        release.SetResult();
        await idle.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.Equal(1, _h.Recorder.Starts);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task UseTranscriber_WhileTranscribing_AppliesFromTheNextDictation()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        _h.Transcriber.Respond = async _ =>
        {
            started.SetResult();
            await release.Task;
            return new TranscriptionResult(Transcript);
        };
        var replacement = new FakeTranscriber();
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await started.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await _h.Ui.RunAsync(() => _h.Controller.UseTranscriber(replacement));
        release.SetResult();
        await idle.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, replacement.Calls);
        Assert.Equal(2, _h.Pastes);
    }

    [Fact]
    public async Task Press_WithoutTranscriber_NeverRecordsAndAsksForAKey()
    {
        var missing = 0;
        _h.Controller.TranscriberMissing += () => missing++;
        await _h.Ui.RunAsync(() => _h.Controller.UseTranscriber(null));

        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);

        Assert.Equal(1, missing);
        Assert.Equal(0, _h.Recorder.Starts);
        Assert.Empty(_h.States);
        Assert.Equal(DictationState.Idle, _h.Controller.State);
    }

    [Fact]
    public async Task UseTranscriber_AfterMissingOne_NextPressDictates()
    {
        await _h.Ui.RunAsync(() => _h.Controller.UseTranscriber(null));
        await _h.Ui.RunAsync(_h.Controller.Press);
        await _h.Ui.RunAsync(_h.Controller.Release);

        await _h.Ui.RunAsync(() => _h.Controller.UseTranscriber(_h.Transcriber));
        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Recorder.Starts);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dispose_DuringTranscription_CancelsItAndInsertsNothing()
    {
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        _h.Transcriber.Respond = async token =>
        {
            seen = token;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new TranscriptionResult(Transcript);
        };
        var idle = _h.NextIdle();
        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await started.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);

        await _h.Ui.RunAsync(_h.Controller.Dispose);
        await idle.WaitAsync(Wait, TestContext.Current.CancellationToken);

        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Clipboard.Calls);
    }

    [Fact]
    public async Task Press_AfterDispose_IsIgnored()
    {
        await _h.Ui.RunAsync(_h.Controller.Dispose);

        await _h.Ui.RunAsync(_h.Controller.Press);

        Assert.Equal(DictationState.Idle, _h.Controller.State);
        Assert.Equal(0, _h.Recorder.Starts);
    }

    private sealed class Harness : IDisposable
    {
        // One second of a loud square wave: speech as far as the silence gate is concerned.
        public static readonly byte[] Speech = Square(16000, 3000);

        public Harness()
        {
            CheckTarget = _ => Decision;
            Clipboard = new FakeClipboard(Original);
            var inserter = new TextInserter(
                Clipboard,
                anyModifierDown: () => false,
                sendPaste: () =>
                {
                    Pastes++;
                    return PasteAccepted;
                },
                typeText: text =>
                {
                    Typed.Add(text);
                    return TypingResult.Typed;
                },
                TimeSpan.Zero);
            Controller = new DictationController(
                Recorder,
                Transcriber,
                inserter,
                captureTarget: () => new WindowTarget(0x1001, 10, 100),
                checkTarget: target => CheckTarget(target),
                Time);
            Controller.StateChanged += States.Add;
            Controller.MicrophoneFailed += MicrophoneErrors.Add;
            Controller.TranscriptionFailed += TranscriptionErrors.Add;
            Controller.TranscriptKept += Kept.Add;
            Controller.NothingHeard += () => NothingHeard++;
        }

        public UiThread Ui { get; } = new();

        public ManualTime Time { get; } = new();

        public FakeRecorder Recorder { get; } = new();

        public FakeTranscriber Transcriber { get; } = new();

        public ClipboardSnapshot Original { get; } = FakeClipboard.TextSnapshot("synthetic previous clipboard");

        public FakeClipboard Clipboard { get; }

        public DictationController Controller { get; }

        public InsertionDecision Decision { get; set; } = InsertionDecision.Insert;

        public Func<WindowTarget, InsertionDecision> CheckTarget { get; set; }

        public bool PasteAccepted { get; set; } = true;

        public int Pastes { get; private set; }

        public int NothingHeard { get; private set; }

        public List<string> Typed { get; } = [];

        public List<DictationState> States { get; } = [];

        public List<MicrophoneError> MicrophoneErrors { get; } = [];

        public List<TranscriptionError> TranscriptionErrors { get; } = [];

        public List<TranscriptKeptReason> Kept { get; } = [];

        /// <summary>Completes the next time the controller returns to Idle.</summary>
        public Task NextIdle()
        {
            var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnState(DictationState state)
            {
                if (state == DictationState.Idle)
                {
                    Controller.StateChanged -= OnState;
                    idle.TrySetResult();
                }
            }

            Controller.StateChanged += OnState;
            return idle.Task;
        }

        /// <summary>Holds the shortcut for <paramref name="hold"/>, releases it, and waits for Idle.</summary>
        public async Task DictateAsync(TimeSpan hold)
        {
            var idle = NextIdle();
            await Ui.RunAsync(Controller.Press);
            Time.Advance(hold);
            await Ui.RunAsync(Controller.Release);
            await idle.WaitAsync(Wait, TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Controller.Dispose();
            Ui.Dispose();
        }

        private static byte[] Square(int samples, short amplitude)
        {
            var pcm = new byte[samples * 2];
            for (var i = 0; i < samples; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), i % 2 == 0 ? amplitude : (short)-amplitude);
            }

            return pcm;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks = TimeSpan.FromHours(1).Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private sealed class FakeRecorder : IAudioRecorder
    {
        private int _starts;

        public int Starts => _starts;

        public MicrophoneFailure? Failure { get; set; }

        public byte[] Pcm { get; set; } = Harness.Speech;

        public Exception? StopError { get; set; }

        public MicrophoneFailure? Start()
        {
            Interlocked.Increment(ref _starts);
            return Failure;
        }

        public Task<byte[]> StopAsync() =>
            StopError is null ? Task.FromResult(Pcm) : Task.FromException<byte[]>(StopError);
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public Func<CancellationToken, Task<TranscriptionResult>> Respond { get; set; } =
            _ => Task.FromResult(new TranscriptionResult(Transcript));

        public int Calls => Uploads.Count;

        public List<byte[]> Uploads { get; } = [];

        public Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
        {
            Uploads.Add(wav);
            return Respond(cancellationToken);
        }
    }
}
