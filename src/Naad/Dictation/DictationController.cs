using System.Diagnostics;
using Naad.Audio;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Native;
using Naad.Transcription;
using NAudio;

namespace Naad.Dictation;

/// <summary>
/// Hold-to-talk flow: press records, release transcribes and pastes. Runs on the UI thread.
/// Logs events and numbers only, never what was said.
/// </summary>
// ponytail: slice-quality flow with plain flags; P9 replaces them with a tested state machine.
internal sealed class DictationController(
    KeyboardHook hook,
    AudioRecorder recorder,
    OpenAiCompatibleTranscriber transcriber) : IDisposable
{
    // Shorter holds are taps or Right Ctrl shortcuts; Whisper tends to invent text for them.
    private static readonly TimeSpan MinimumHold = TimeSpan.FromMilliseconds(300);

    private readonly CancellationTokenSource _shutdown = new();
    private Task<bool>? _starting;
    private nint _targetWindow;
    private long _pressedAt;
    private bool _recording;
    private LogEvent? _cancelledBy;
    private bool _busy;

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

    private void OnPressed()
    {
        // One dictation at a time: presses while transcribing are ignored.
        if (_recording || _busy)
        {
            return;
        }

        _targetWindow = User32.GetForegroundWindow();
        _recording = true;
        _cancelledBy = null;
        _pressedAt = Stopwatch.GetTimestamp();
        _starting = StartRecordingAsync();
    }

    // Opening the microphone can take long enough that the keyboard hook, which shares
    // this thread, misses the key release of a quick tap. So it opens on the thread pool.
    private async Task<bool> StartRecordingAsync()
    {
        try
        {
            await Task.Run(recorder.Start);
        }
        catch (MmException e)
        {
            Log.Warn(LogEvent.MicrophoneUnavailable, ("mm_result", (long)e.Result));
            return false;
        }

        Log.Info(LogEvent.RecordingStarted, ("mic_open_ms", Milliseconds(Stopwatch.GetElapsedTime(_pressedAt))));
        return true;
    }

    // Another key or a mouse button joined the shortcut (Right Ctrl+C, Ctrl+click). No release
    // follows, so the recording stops now and is discarded.
    private void OnCancelled(CancelCause cause)
    {
        if (_recording)
        {
            _cancelledBy = cause == CancelCause.Mouse
                ? LogEvent.DictationCancelledByMouse
                : LogEvent.DictationCancelledByKey;
            OnReleased();
        }
    }

    private void OnReleased()
    {
        if (!_recording)
        {
            return;
        }

        _recording = false;
        _busy = true;
        _ = FinishAsync(Stopwatch.GetTimestamp());
    }

    private async Task FinishAsync(long releasedAt)
    {
        // Read before the first await: Dispose may dispose the source later.
        var shutdown = _shutdown.Token;
        try
        {
            var held = Stopwatch.GetElapsedTime(_pressedAt, releasedAt);
            if (!await _starting!)
            {
                return;
            }

            var pcm = await recorder.StopAsync();
            if (_cancelledBy is { } cancelled)
            {
                Log.Info(cancelled);
                return;
            }

            if (held < MinimumHold)
            {
                Log.Info(LogEvent.DictationIgnoredShortHold, ("held_ms", Milliseconds(held)));
                return;
            }

            var wav = Wav.Encode(pcm, AudioRecorder.Format);
            Log.Info(LogEvent.RecordingStopped, ("held_ms", Milliseconds(held)), ("wav_bytes", wav.Length));

            var requestStart = Stopwatch.GetTimestamp();
            var text = await transcriber.TranscribeAsync(wav, shutdown);
            Log.Info(
                LogEvent.TranscriptionCompleted,
                ("request_ms", Milliseconds(Stopwatch.GetElapsedTime(requestStart))),
                ("chars", text.Length));
            if (text.Length == 0)
            {
                Log.Info(LogEvent.TranscriptionEmpty);
                return;
            }

            var modifiersReleased = await TextInserter.WaitForModifiersReleasedAsync();
            if (shutdown.IsCancellationRequested)
            {
                return;
            }

            Insert(text, modifiersReleased, releasedAt);
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
            // This task is fire-and-forget, so anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
        }
        finally
        {
            _busy = false;
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
