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
/// Reports events and numbers only, never what was said.
/// </summary>
// ponytail: slice-quality flow with plain flags and console output; P9 replaces the flags
// with a tested state machine and P5 replaces Report with the log.
internal sealed class DictationController(
    KeyboardHook hook,
    AudioRecorder recorder,
    OpenAiCompatibleTranscriber transcriber)
{
    // Shorter holds are taps or Right Ctrl shortcuts; Whisper tends to invent text for them.
    private static readonly TimeSpan MinimumHold = TimeSpan.FromMilliseconds(300);

    private Task<bool>? _starting;
    private nint _targetWindow;
    private long _pressedAt;
    private bool _recording;
    private bool _cancelled;
    private bool _busy;

    public void Listen()
    {
        hook.TriggerPressed += OnPressed;
        hook.OtherKeyPressed += OnOtherKeyPressed;
        hook.TriggerReleased += OnReleased;
    }

    private static void Report(string message) =>
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff}  {message}");

    private void OnPressed()
    {
        // One dictation at a time: presses while transcribing are ignored.
        if (_recording || _busy)
        {
            return;
        }

        _targetWindow = User32.GetForegroundWindow();
        _recording = true;
        _cancelled = false;
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
            Report($"Microphone unavailable ({e.Result}). Check Settings > Privacy & security > Microphone.");
            return false;
        }

        Report($"Recording (microphone opened in {Stopwatch.GetElapsedTime(_pressedAt).TotalMilliseconds:F0} ms)...");
        return true;
    }

    private void OnOtherKeyPressed()
    {
        if (_recording)
        {
            _cancelled = true;
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
        try
        {
            var held = Stopwatch.GetElapsedTime(_pressedAt, releasedAt);
            if (!await _starting!)
            {
                return;
            }

            var pcm = await recorder.StopAsync();
            if (_cancelled)
            {
                Report("Cancelled: another key was pressed with Right Ctrl.");
                return;
            }

            if (held < MinimumHold)
            {
                Report($"Ignored: held for {held.TotalMilliseconds:F0} ms.");
                return;
            }

            var wav = Wav.Encode(pcm, AudioRecorder.Format);
            Report($"Recorded {held.TotalSeconds:F1} s ({wav.Length:N0} bytes). Transcribing...");

            var requestStart = Stopwatch.GetTimestamp();
            var text = await transcriber.TranscribeAsync(wav, CancellationToken.None);
            var requestTime = Stopwatch.GetElapsedTime(requestStart);
            Report($"Transcribed {text.Length} characters in {requestTime.TotalMilliseconds:F0} ms.");
            if (text.Length == 0)
            {
                Report("Nothing to insert.");
                return;
            }

            Insert(text, await TextInserter.WaitForModifiersReleasedAsync());
            Report($"Release to text: {Stopwatch.GetElapsedTime(releasedAt).TotalMilliseconds:F0} ms.");
        }
        catch (HttpRequestException e)
        {
            Report(e.StatusCode is { } status
                ? $"Transcription failed: HTTP {(int)status}."
                : $"Transcription failed: network error ({e.HttpRequestError}).");
        }
        catch (OperationCanceledException)
        {
            Report("Transcription timed out.");
        }
        catch (Exception e)
        {
            // This task is fire-and-forget, so anything not caught here would vanish silently.
            Report($"Dictation failed: {e.GetType().Name}.");
        }
        finally
        {
            _busy = false;
        }
    }

    // Never paste into a window the user didn't dictate into; the transcript stays on
    // the clipboard so nothing is lost.
    private void Insert(string text, bool modifiersReleased)
    {
        TextInserter.CopyToClipboard(text);
        if (User32.GetForegroundWindow() != _targetWindow)
        {
            Report("Focus changed while transcribing: not pasted. The transcript is on the clipboard.");
        }
        else if (!modifiersReleased)
        {
            Report("Modifier keys still held: not pasted. The transcript is on the clipboard.");
        }
        else if (!TextInserter.SendPaste())
        {
            Report("Windows rejected the paste. The transcript is on the clipboard.");
        }
        else
        {
            Report("Pasted.");
        }
    }
}
