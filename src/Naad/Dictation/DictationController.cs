using System.Diagnostics;
using Naad.Audio;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>
/// Hold-to-talk flow: press records, release transcribes and pastes. <see cref="DictationSession"/>
/// decides what happens; this class carries it out. Runs on the UI thread: every await resumes
/// there, so the session is only ever touched from one thread. Logs events and numbers only,
/// never what was said.
/// </summary>
internal sealed class DictationController(
    KeyboardHook hook,
    AudioRecorder recorder,
    ITranscriber transcriber,
    TextInserter inserter) : IDisposable
{
    private readonly DictationSession _session = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<bool> _starting = Task.FromResult(false);

    // Completes once the last recording has fully stopped. A new recording waits for it, so a
    // quick press after a discarded one never opens the microphone while it is still closing.
    private Task _stopped = Task.CompletedTask;
    private WindowTarget _target = WindowTarget.None;

    // The last transcript that couldn't be inserted, for the user to copy. Memory only, one at a
    // time: a newer kept transcript replaces it, and it is gone when Naad exits.
    private string? _keptTranscript;

    /// <summary>The microphone could not be opened for a dictation. Raised on the UI thread.</summary>
    public event Action<MicrophoneError>? MicrophoneFailed;

    /// <summary>
    /// A transcript wasn't inserted and is kept for <see cref="CopyKeptTranscriptAsync"/>. Raised
    /// on the UI thread.
    /// </summary>
    public event Action<TranscriptKeptReason>? TranscriptKept;

    public void Listen()
    {
        hook.TriggerPressed += OnPressed;
        hook.TriggerCancelled += OnCancelled;
        hook.TriggerReleased += OnReleased;
    }

    /// <summary>
    /// Stops accepting new dictations and cancels a transcription in flight, so nothing is
    /// pasted once shutdown starts. Dispose before the hook and recorder it uses.
    /// </summary>
    public void Dispose()
    {
        hook.TriggerPressed -= OnPressed;
        hook.TriggerCancelled -= OnCancelled;
        hook.TriggerReleased -= OnReleased;
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void OnPressed() => Apply(DictationEvent.Pressed, _session.Job);

    private void OnReleased() => Apply(DictationEvent.Released, _session.Job);

    private void OnCancelled(CancelCause cause) => Apply(
        cause == CancelCause.Mouse ? DictationEvent.CancelledByMouse : DictationEvent.CancelledByKey,
        _session.Job);

    private DictationStep Apply(DictationEvent e, long job)
    {
        var step = _session.Handle(e, job, Stopwatch.GetElapsedTime(0));
        switch (step.Action)
        {
            case DictationAction.StartRecording:
                _target = FocusGuard.Foreground();
                _starting = StartRecordingAsync(step.Job, _stopped, Stopwatch.GetTimestamp());
                _ = StopAtMaxDurationAsync(step.Job, _shutdown.Token);
                break;
            case DictationAction.Discard:
                _ = DiscardAsync(e, step.Held);
                break;
            case DictationAction.Transcribe:
                _ = TranscribeAsync(step, Stopwatch.GetTimestamp());
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
            LogFailure(f);
            Apply(DictationEvent.RecordingFailed, job);
            MicrophoneFailed?.Invoke(f.Error);
            return false;
        }

        Log.Info(LogEvent.RecordingStarted, ("mic_open_ms", Milliseconds(Stopwatch.GetElapsedTime(pressedAt))));
        return true;
    }

    private static void LogFailure(MicrophoneFailure failure)
    {
        switch (failure.Error)
        {
            case MicrophoneError.NotFound:
                Log.Warn(LogEvent.MicrophoneNotFound, ("mm_result", failure.Code));
                break;
            case MicrophoneError.Blocked:
                Log.Warn(LogEvent.MicrophoneBlocked);
                break;
            case MicrophoneError.InUse:
                Log.Warn(LogEvent.MicrophoneInUse, ("mm_result", failure.Code));
                break;
            default:
                Log.Warn(LogEvent.MicrophoneUnavailable, ("mm_result", failure.Code));
                break;
        }
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
            await Task.Delay(DictationSession.MaxRecording, shutdown);
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
            if (await StopRecording() is null)
            {
                return;
            }
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
            return;
        }

        switch (cause)
        {
            case DictationEvent.Released:
                Log.Info(LogEvent.DictationIgnoredShortHold, ("held_ms", Milliseconds(held)));
                break;
            case DictationEvent.CancelledByMouse:
                Log.Info(LogEvent.DictationCancelledByMouse);
                break;
            default:
                Log.Info(LogEvent.DictationCancelledByKey);
                break;
        }
    }

    private async Task TranscribeAsync(DictationStep step, long releasedAt)
    {
        // Read before the first await: Dispose may dispose the source later.
        var shutdown = _shutdown.Token;
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
                Log.Info(
                    check.Verdict == RecordingVerdict.TooShort ? LogEvent.RecordingTooShort : LogEvent.RecordingSilent,
                    ("audio_ms", Milliseconds(check.Duration)),
                    ("peak", check.Peak),
                    ("rms", check.Rms));
                return;
            }

            var wav = Wav.Encode(pcm, AudioRecorder.Format);
            Log.Info(
                LogEvent.RecordingStopped,
                ("held_ms", Milliseconds(step.Held)),
                ("wav_bytes", wav.Length),
                ("peak", check.Peak),
                ("rms", check.Rms));

            var requestStart = Stopwatch.GetTimestamp();
            var result = await transcriber.TranscribeAsync(wav, shutdown);
            if (result.Failure is { } failure)
            {
                LogFailure(failure);
                return;
            }

            var text = result.Text;
            Log.Info(
                LogEvent.TranscriptionCompleted,
                ("request_ms", Milliseconds(Stopwatch.GetElapsedTime(requestStart))),
                ("chars", text.Length));
            if (text.Length == 0)
            {
                Log.Info(LogEvent.TranscriptionEmpty);
                Apply(DictationEvent.TranscribedEmpty, job);
                return;
            }

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

    private static void LogFailure(TranscriptionFailure failure)
    {
        switch (failure.Error)
        {
            case TranscriptionError.Timeout:
                Log.Warn(LogEvent.TranscriptionTimedOut);
                break;
            case TranscriptionError.MalformedResponse:
                Log.Warn(LogEvent.TranscriptionMalformedResponse);
                break;
            case TranscriptionError.Network:
                Log.Warn(LogEvent.TranscriptionFailed, ("network_error", failure.Code));
                break;
            default:
                Log.Warn(LogEvent.TranscriptionFailed, ("http_status", failure.Code));
                break;
        }
    }

    // Never paste into a window the user didn't dictate into; the transcript stays on
    // the clipboard so nothing is lost.
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

    // Text goes only into the window the user dictated into, and only if it can take input.
    // Otherwise the transcript is kept and the user is told; focus is never switched.
    private async Task InsertAsync(string text, WindowTarget target, long releasedAt, CancellationToken shutdown)
    {
        var decision = FocusGuard.Check(target);
        if (decision != InsertionDecision.Insert)
        {
            KeepForDecision(text, decision);
            return;
        }

        var outcome = await inserter.PasteAsync(
            text,
            canInsert: () => FocusGuard.Check(target) == InsertionDecision.Insert,
            pasted: () => Log.Info(
                LogEvent.Pasted, ("release_to_text_ms", Milliseconds(Stopwatch.GetElapsedTime(releasedAt)))),
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
                var now = FocusGuard.Check(target);
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

    private static long Milliseconds(TimeSpan duration) => (long)duration.TotalMilliseconds;
}
