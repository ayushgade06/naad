using System.Buffers.Binary;
using Naad.Audio;
using Naad.Cleanup;
using Naad.Dictation;
using Naad.Insertion;
using Naad.Tests.Insertion;
using Naad.Transcription;

namespace Naad.Tests.Dictation;

// The real controller with fakes only at the edges: the microphone, the transcription service,
// the clipboard, input and focus. Shared by the controller's test classes.
internal sealed class Harness : IDisposable
{
    public const string Transcript = "synthetic transcript";
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

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
        Controller.MicrophoneOpened += () => Recorder.Calls.Add("opened");
        Controller.MicrophoneClosed += () => Recorder.Calls.Add("closed");
        Controller.CleaningUp += _ => Cleanups++;
        Controller.CleanupFellBack += () => CleanupFallbacks++;
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

    public int Cleanups { get; private set; }

    public int CleanupFallbacks { get; private set; }

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

internal sealed class ManualTime : TimeProvider
{
    private long _ticks = TimeSpan.FromHours(1).Ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

internal sealed class FakeRecorder : IAudioRecorder
{
    private int _starts;

    public int Starts => _starts;

    public MicrophoneFailure? Failure { get; set; }

    public byte[] Pcm { get; set; } = Harness.Speech;

    public Exception? StopError { get; set; }

    // Device calls and the controller's microphone events, in order. They never overlap.
    public List<string> Calls { get; } = [];

    public MicrophoneFailure? Start()
    {
        Interlocked.Increment(ref _starts);
        Calls.Add("start");
        return Failure;
    }

    public Task<byte[]> StopAsync()
    {
        Calls.Add("stop");
        return StopError is null ? Task.FromResult(Pcm) : Task.FromException<byte[]>(StopError);
    }
}

internal sealed class FakeTranscriber : ITranscriber
{
    public Func<CancellationToken, Task<TranscriptionResult>> Respond { get; set; } =
        _ => Task.FromResult(new TranscriptionResult(Harness.Transcript));

    public int Calls => Uploads.Count;

    public List<byte[]> Uploads { get; } = [];

    public Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        Uploads.Add(wav);
        return Respond(cancellationToken);
    }
}

internal sealed class FakeCleanup(CleanupResult result) : ITranscriptCleanup
{
    public bool UsesModel { get; init; } = true;

    public Exception? Error { get; init; }

    public int Prepared { get; private set; }

    public List<string> Cleaned { get; } = [];

    public List<Naad.Context.DictationContext> Contexts { get; } = [];

    public void Prepare() => Prepared++;

    public Task<CleanupResult> CleanAsync(
        string transcript, Naad.Context.DictationContext context, CancellationToken cancellationToken)
    {
        Cleaned.Add(transcript);
        Contexts.Add(context);
        return Error is null ? Task.FromResult(result) : Task.FromException<CleanupResult>(Error);
    }
}
