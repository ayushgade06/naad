using System.Diagnostics;
using Naad.Audio;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>
/// Hold-to-talk flow: press records, release transcribes and inserts. <see cref="DictationSession"/>
/// decides what happens; this class carries it out through its collaborators and holds no Win32,
/// HTTP or UI code. Runs on the UI thread: every await resumes there, so the session is only ever
/// touched from one thread. Logs events and numbers only, never what was said.
/// </summary>
/// <param name="recorder">The microphone.</param>
/// <param name="transcriber">Turns the recording into text; null until an API key is set. See
/// <see cref="UseTranscriber"/>.</param>
/// <param name="inserter">Puts the text into the target window.</param>
/// <param name="captureTarget">The window that has focus, read when the shortcut is pressed.</param>
/// <param name="checkTarget">Whether that window may receive text now.</param>
/// <param name="time">Measures holds and durations; tests replace it.</param>
internal sealed class DictationController(
    IAudioRecorder recorder,
    ITranscriber? transcriber,
    TextInserter inserter,
    Func<WindowTarget> captureTarget,
    Func<WindowTarget, InsertionDecision> checkTarget,
    TimeProvider time) : IDisposable
{
    private readonly DictationSession _session = new();
    private readonly CancellationTokenSource _shutdown = new();
    private ITranscriber? _transcriber = transcriber;
    private bool _handsFree;
    private Task<bool> _starting = Task.FromResult(false);

    // Completes once the last recording has fully stopped. A new recording waits for it, so a
    // quick press after a discarded one never opens the microphone while it is still closing.
    private Task _stopped = Task.CompletedTask;
    private WindowTarget _target = WindowTarget.None;
    private bool _disposed;

    // The last transcript that couldn't be inserted, for the user to copy. Memory only, one at a
    // time: a newer kept transcript replaces it, and it is gone when Naad exits.
    private string? _keptTranscript;

    /// <summary>The dictation state changed. Raised on the UI thread.</summary>
    public event Action<DictationState>? StateChanged;

    /// <summary>The microphone could not be opened for a dictation. Raised on the UI thread.</summary>
    public event Action<MicrophoneError>? MicrophoneFailed;

    /// <summary>A recording held no sound worth uploading. Raised on the UI thread.</summary>
    public event Action? NothingHeard;

    /// <summary>
    /// The shortcut was pressed but there is no transcriber, so nothing was recorded: the user
    /// must add an API key first. Raised on the UI thread.
    /// </summary>
    public event Action? TranscriberMissing;

    /// <summary>The transcription service failed. Raised on the UI thread.</summary>
    public event Action<TranscriptionError>? TranscriptionFailed;

    /// <summary>
    /// A transcript wasn't inserted and is kept for <see cref="CopyKeptTranscriptAsync"/>. Raised
    /// on the UI thread.
    /// </summary>
    public event Action<TranscriptKeptReason>? TranscriptKept;

    public DictationState State => _session.State;

    /// <summary>The shortcut went down. Call on the UI thread.</summary>
    public void Press() => ApplyShortcut(DictationEvent.Pressed);

    /// <summary>The shortcut was released. Call on the UI thread.</summary>
    public void Release() => ApplyShortcut(DictationEvent.Released);

    /// <summary>Another key or a mouse button joined the shortcut. Call on the UI thread.</summary>
    public void Cancel(CancelCause cause) => ApplyShortcut(
        cause == CancelCause.Mouse ? DictationEvent.CancelledByMouse : DictationEvent.CancelledByKey);

    /// <summary>
    /// Transcribes with <paramref name="replacement"/> from the next recording on, after a settings
    /// or key change. A transcription already running finishes with the one it started with. Call
    /// on the UI thread.
    /// </summary>
    public void UseTranscriber(ITranscriber? replacement) => _transcriber = replacement;

    /// <summary>
    /// Hands-free: a press starts recording and the next press stops it; releasing the shortcut
    /// does nothing. Otherwise the shortcut is held while speaking. Call on the UI thread.
    /// </summary>
    public void UseHandsFree(bool handsFree) => _handsFree = handsFree;

    /// <summary>
    /// Puts the kept transcript on the clipboard, out of clipboard history, for the user to
    /// paste themselves. Naad never pastes it: the user may be anywhere by then, and the tray
    /// menu itself takes focus. Returns false if there is none or the clipboard is busy.
    /// Call on the UI thread.
    /// </summary>
    public async Task<bool> CopyKeptTranscriptAsync()
    {
        if (_keptTranscript is not { } text)
        {
            return false;
        }

        if (!await inserter.CopyAsync(text))
        {
            Log.Warn(LogEvent.TranscriptCopyFailed);
            return false;
        }

        Log.Info(LogEvent.TranscriptCopied, ("chars", text.Length));
        return true;
    }

    /// <summary>
    /// Stops accepting new dictations and cancels a transcription or paste in flight, so nothing
    /// is inserted once shutdown starts. Dispose before the recorder it uses.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    // Shortcut events always apply to the current job; after shutdown they are ignored.
    private void ApplyShortcut(DictationEvent e)
    {
        if (_disposed)
        {
            return;
        }

        // Hands-free maps its second press onto the release that ends a held dictation, so the
        // session's rules (short-hold check, time limit, cancels) stay the same in both modes.
        if (_handsFree)
        {
            if (e == DictationEvent.Released)
            {
                return;
            }

            if (e == DictationEvent.Pressed && _session.State == DictationState.Recording)
            {
                e = DictationEvent.Released;
            }
        }

        // Without a transcriber the words could never be used, so the microphone isn't opened.
        if (e == DictationEvent.Pressed && _transcriber is null && _session.State == DictationState.Idle)
        {
            Log.Warn(LogEvent.DictationNeedsApiKey);
            TranscriberMissing?.Invoke();
            return;
        }

        Apply(e, _session.Job);
    }

    private DictationStep Apply(DictationEvent e, long job)
    {
        var before = _session.State;
        var step = _session.Handle(e, job, time.GetElapsedTime(0));

        // Raised before the action runs: an action can finish synchronously and apply further
        // events, whose changes must be reported after this one, not before it.
        if (_session.State != before)
        {
            StateChanged?.Invoke(_session.State);
        }

        switch (step.Action)
        {
            case DictationAction.StartRecording:
                _target = captureTarget();
                _starting = StartRecordingAsync(step.Job, _stopped, time.GetTimestamp());
                _ = StopAtMaxDurationAsync(step.Job, _shutdown.Token);
                break;
            case DictationAction.Discard:
                _ = DiscardAsync(e, step.Held);
                break;
            case DictationAction.Transcribe:
                _ = TranscribeAsync(step, time.GetTimestamp());
                break;
        }

        return step;
    }

    // Opening the microphone can take long enough that the keyboard hook, which shares
    // this thread, misses the key release of a quick tap. So it opens on the thread pool.
    private async Task<bool> StartRecordingAsync(long job, Task previousStop, long pressedAt)
    {
        // The previous recording's own task reports how its stop went.
        await previousStop.ConfigureAwait(
            ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        MicrophoneFailure? failure;
        try
        {
            failure = await Task.Run(recorder.Start);
        }
        catch (ObjectDisposedException)
        {
            // Naad is shutting down: the recorder was disposed while this press was queued.
            return false;
        }

        if (failure is { } f)
        {
            DictationLog.Microphone(f);
            Apply(DictationEvent.RecordingFailed, job);
            MicrophoneFailed?.Invoke(f.Error);
            return false;
        }

        Log.Info(LogEvent.RecordingStarted, ("mic_open_ms", Milliseconds(time.GetElapsedTime(pressedAt))));
        return true;
    }

    // Returns the recorded PCM, or null if the microphone never opened.
    private Task<byte[]?> StopRecording()
    {
        var stop = StopAfterStartAsync(_starting);
        _stopped = stop;
        return stop;
    }

    private async Task<byte[]?> StopAfterStartAsync(Task<bool> starting) =>
        await starting ? await recorder.StopAsync() : null;

    // ponytail: every recording leaves a five-minute timer behind, ignored by job id once the
    // recording ends; cancel it per job if many short dictations ever show up in memory use.
    private async Task StopAtMaxDurationAsync(long job, CancellationToken shutdown)
    {
        try
        {
            await Task.Delay(DictationSession.MaxRecording, time, shutdown);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Apply(DictationEvent.MaxDurationReached, job).Action == DictationAction.Transcribe)
        {
            Log.Info(LogEvent.RecordingMaxDurationReached);
        }
    }

    private async Task DiscardAsync(DictationEvent cause, TimeSpan held)
    {
        try
        {
            if (await StopRecording() is not null)
            {
                DictationLog.Discarded(cause, held);
            }
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
        }
    }

    private async Task TranscribeAsync(DictationStep step, long releasedAt)
    {
        // Read before the first await: Dispose may dispose the source later, and a transcriber
        // swapped in meanwhile is for the next dictation.
        var shutdown = _shutdown.Token;
        var service = _transcriber;
        var job = step.Job;

        // The window the user was in when they pressed the shortcut, never whatever has focus now.
        var target = _target;
        try
        {
            // Null: the microphone never opened, and the session already ended the job.
            if (await StopRecording() is not { } pcm)
            {
                return;
            }

            // Silence and slivers of audio are never uploaded: Whisper invents text for them.
            // Returning ends the job in the finally block below.
            var check = SilenceGate.Check(pcm, AudioRecorder.Format);
            if (check.Verdict != RecordingVerdict.Accepted)
            {
                DictationLog.Rejected(check);
                if (check.Verdict == RecordingVerdict.Silent)
                {
                    NothingHeard?.Invoke();
                }

                return;
            }

            var wav = Wav.Encode(pcm, AudioRecorder.Format);
            Log.Info(
                LogEvent.RecordingStopped,
                ("held_ms", Milliseconds(step.Held)),
                ("wav_bytes", wav.Length),
                ("peak", check.Peak),
                ("rms", check.Rms));

            var requestStart = time.GetTimestamp();
            // A recording only starts with a transcriber, and one is never removed mid-dictation.
            var result = await (service ?? throw new InvalidOperationException("No transcriber."))
                .TranscribeAsync(wav, shutdown);
            if (result.Failure is { } failure)
            {
                DictationLog.Transcription(failure);
                TranscriptionFailed?.Invoke(failure.Error);
                return;
            }

            var text = result.Text;
            Log.Info(
                LogEvent.TranscriptionCompleted,
                ("request_ms", Milliseconds(time.GetElapsedTime(requestStart))),
                ("chars", text.Length));
            if (text.Length == 0)
            {
                Log.Info(LogEvent.TranscriptionEmpty);
                Apply(DictationEvent.TranscribedEmpty, job);
                return;
            }

            // A late or repeated result for an ended job is refused here, so nothing is
            // inserted twice.
            if (Apply(DictationEvent.Transcribed, job).Action != DictationAction.Insert)
            {
                return;
            }

            await InsertAsync(text, target, releasedAt, shutdown);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Log.Info(LogEvent.DictationCancelledByShutdown);
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
        }
        finally
        {
            // Every path ends the job, so the next press is accepted. Events for a job that
            // already ended are ignored by the session.
            var end = _session.State == DictationState.Inserting
                ? DictationEvent.Inserted
                : DictationEvent.TranscriptionFailed;
            Apply(end, job);
        }
    }

    // Text goes only into the window the user dictated into, and only if it can take input.
    // Otherwise the transcript is kept and the user is told; focus is never switched.
    private async Task InsertAsync(string text, WindowTarget target, long releasedAt, CancellationToken shutdown)
    {
        var decision = checkTarget(target);
        if (decision != InsertionDecision.Insert)
        {
            KeepForDecision(text, decision);
            return;
        }

        var outcome = await inserter.PasteAsync(
            text,
            canInsert: () => checkTarget(target) == InsertionDecision.Insert,
            pasted: () => Log.Info(
                LogEvent.Pasted, ("release_to_text_ms", Milliseconds(time.GetElapsedTime(releasedAt)))),
            shutdown);
        if (inserter.LastSkippedFormats > 0 && outcome is InsertionOutcome.Pasted)
        {
            Log.Warn(LogEvent.ClipboardFormatsSkipped, ("formats", inserter.LastSkippedFormats));
        }

        switch (outcome)
        {
            case InsertionOutcome.Pasted:
                break;
            case InsertionOutcome.PastedClipboardChanged:
                Log.Info(LogEvent.ClipboardRestoreSkippedChanged);
                break;
            case InsertionOutcome.PastedRestoreFailed:
                Log.Warn(LogEvent.ClipboardRestoreFailed);
                break;
            case InsertionOutcome.Typed:
                Log.Info(LogEvent.InsertedByTyping, ("chars", text.Length));
                break;
            case InsertionOutcome.TargetChanged:
                // Focus moved while waiting for the modifiers; say why as precisely as possible.
                var now = checkTarget(target);
                KeepForDecision(text, now == InsertionDecision.Insert ? InsertionDecision.FocusChanged : now);
                break;
            case InsertionOutcome.ModifiersHeld:
                Keep(text, TranscriptKeptReason.NotInserted, LogEvent.PasteSkippedModifiersHeld);
                break;
            case InsertionOutcome.PasteRejected:
                Keep(text, TranscriptKeptReason.NotInserted, LogEvent.PasteRejected);
                break;
            case InsertionOutcome.ClipboardUnavailable:
                Keep(text, TranscriptKeptReason.NotInserted, LogEvent.ClipboardUnavailable);
                break;
            case InsertionOutcome.TypedPartially:
                Keep(text, TranscriptKeptReason.NotInserted, LogEvent.TypingIncomplete);
                break;
            case InsertionOutcome.Cancelled:
                Log.Info(LogEvent.DictationCancelledByShutdown);
                break;
            default:
                // The session allows one insertion at a time, so the inserter is never busy here.
                throw new UnreachableException($"Unexpected insertion outcome {outcome}.");
        }
    }

    private void KeepForDecision(string text, InsertionDecision decision)
    {
        switch (decision)
        {
            case InsertionDecision.TargetElevated:
                Keep(text, TranscriptKeptReason.TargetElevated, LogEvent.PasteSkippedTargetElevated);
                break;
            case InsertionDecision.TargetClosed:
                Keep(text, TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedTargetClosed);
                break;
            case InsertionDecision.NoTarget:
                Keep(text, TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedNoTarget);
                break;
            default:
                Keep(text, TranscriptKeptReason.WindowChanged, LogEvent.PasteSkippedFocusChanged);
                break;
        }
    }

    private void Keep(string text, TranscriptKeptReason reason, LogEvent logEvent)
    {
        _keptTranscript = text;
        Log.Warn(logEvent);
        TranscriptKept?.Invoke(reason);
    }

    private static long Milliseconds(TimeSpan duration) => DictationLog.Milliseconds(duration);
}
