using System.Diagnostics;
using Naad.Audio;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Native;
using Naad.Transcription;
using NAudio;

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
    OpenAiCompatibleTranscriber transcriber) : IDisposable
{
    private readonly DictationSession _session = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<bool> _starting = Task.FromResult(false);

    // Completes once the last recording has fully stopped. A new recording waits for it, so a
    // quick press after a discarded one never opens the microphone while it is still closing.
    private Task _stopped = Task.CompletedTask;
    private nint _targetWindow;

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
                _targetWindow = User32.GetForegroundWindow();
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
        try
        {
            await Task.Run(recorder.Start);
        }
        catch (MmException e)
        {
            Log.Warn(LogEvent.MicrophoneUnavailable, ("mm_result", (long)e.Result));
            Apply(DictationEvent.RecordingFailed, job);
            return false;
        }

        Log.Info(LogEvent.RecordingStarted, ("mic_open_ms", Milliseconds(Stopwatch.GetElapsedTime(pressedAt))));
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
        try
        {
            // Null: the microphone never opened, and the session already ended the job.
            if (await StopRecording() is not { } pcm)
            {
                return;
            }

            var wav = Wav.Encode(pcm, AudioRecorder.Format);
            Log.Info(LogEvent.RecordingStopped, ("held_ms", Milliseconds(step.Held)), ("wav_bytes", wav.Length));

            var requestStart = Stopwatch.GetTimestamp();
            var text = await transcriber.TranscribeAsync(wav, shutdown);
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

            var modifiersReleased = await TextInserter.WaitForModifiersReleasedAsync();
            if (!shutdown.IsCancellationRequested)
            {
                Insert(text, modifiersReleased, releasedAt);
            }
        }
        catch (HttpRequestException e)
        {
            if (e.StatusCode is { } status)
            {
                Log.Warn(LogEvent.TranscriptionFailed, ("http_status", (long)status));
            }
            else
            {
                Log.Warn(LogEvent.TranscriptionFailed, ("network_error", (long)e.HttpRequestError));
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            Log.Info(LogEvent.DictationCancelledByShutdown);
        }
        catch (OperationCanceledException)
        {
            Log.Warn(LogEvent.TranscriptionTimedOut);
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

    // Never paste into a window the user didn't dictate into; the transcript stays on
    // the clipboard so nothing is lost.
    private void Insert(string text, bool modifiersReleased, long releasedAt)
    {
        TextInserter.CopyToClipboard(text);
        if (User32.GetForegroundWindow() != _targetWindow)
        {
            Log.Warn(LogEvent.PasteSkippedFocusChanged);
        }
        else if (!modifiersReleased)
        {
            Log.Warn(LogEvent.PasteSkippedModifiersHeld);
        }
        else if (!TextInserter.SendPaste())
        {
            Log.Warn(LogEvent.PasteRejected);
        }
        else
        {
            Log.Info(LogEvent.Pasted, ("release_to_text_ms", Milliseconds(Stopwatch.GetElapsedTime(releasedAt))));
        }
    }

    private static long Milliseconds(TimeSpan duration) => (long)duration.TotalMilliseconds;
}
