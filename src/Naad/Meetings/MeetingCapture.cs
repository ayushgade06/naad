using Naad.Audio;
using NAudio;
using NAudio.Wave;

namespace Naad.Meetings;

/// <summary>
/// A meeting's two tracks: the microphone (the user) through WinMM, and the PC's sound (everyone
/// else) through WASAPI process loopback of every app except Naad, so its own tones are never
/// transcribed as the other side. Both arrive as 16 kHz mono PCM16 on their capture threads.
/// A microphone another app silences, or one that is unplugged, is reopened on the Windows
/// default until it works again. Audio stays in memory. Create with <see cref="CreateAsync"/>.
/// </summary>
internal sealed class MeetingCapture : IMeetingCapture
{
    // Larger buffers than dictation's: nothing waits on the last few milliseconds here.
    private const int BufferMilliseconds = 100;

    // WAVE_MAPPER: a reopened microphone follows the Windows default, which changes when the
    // chosen one is unplugged.
    private const int DefaultDevice = -1;

    // The last buffers arrive within milliseconds of a stop; a driver that never confirms it
    // must not hang the end of a meeting.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly WasapiRecorder _speakers;
    private readonly PcmConverter _converter;
    private readonly MicrophoneWatchdog _watchdog = new();
    private readonly Action<Speaker, byte[]> _captured;
    private readonly Action<bool> _microphoneBlocked;
    private readonly long _created = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly TaskCompletionSource _speakersDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WaveIn _microphone;
    private TaskCompletionSource _microphoneDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stopping;
    private int _reopening;

    private MeetingCapture(
        WasapiRecorder speakers, int microphone, Action<Speaker, byte[]> captured, Action<bool> microphoneBlocked)
    {
        _speakers = speakers;
        _converter = new(speakers.WaveFormat);
        _captured = captured;
        _microphoneBlocked = microphoneBlocked;
        _microphone = Microphone(microphone, _microphoneDone);

        // Raised only on the loopback's own capture thread, so the converter has one caller.
        _speakers.DataAvailable += (buffer, _, _, _) =>
        {
            var pcm = _converter.Convert(buffer);
            if (pcm.Length > 0)
            {
                captured(Speaker.Others, pcm);
            }
        };
        _speakers.RecordingStopped += (_, _) => _speakersDone.TrySetResult();
    }

    /// <summary>Opens both tracks, ready to <see cref="Start"/>.</summary>
    /// <param name="microphone">The WinMM device number, or -1 for the Windows default.</param>
    /// <param name="captured">Each buffer of a track, raised on that track's capture thread.</param>
    /// <param name="microphoneBlocked">The microphone went silent or away (true), or works again
    /// (false). Raised on a capture or thread-pool thread.</param>
    /// <exception cref="System.Runtime.InteropServices.COMException">The PC's sound can't be captured.</exception>
    /// <exception cref="NotSupportedException">The PC's sound comes in a format Naad can't read.</exception>
    public static async Task<IMeetingCapture> CreateAsync(
        int microphone, Action<Speaker, byte[]> captured, Action<bool> microphoneBlocked)
    {
        // Process loopback has no mix format of its own, so it is asked for Whisper's directly;
        // the converter still handles whatever arrives. Windows 10 before 2004 lacks it, and
        // captures the whole mix, Naad's tones included.
        var builder = new WasapiRecorderBuilder();
        builder = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? builder.WithProcessLoopback(
                (uint)Environment.ProcessId, NAudio.CoreAudioApi.ProcessLoopbackMode.ExcludeTargetProcessTree)
                .WithFormat(AudioRecorder.Format)
            : builder.WithLoopbackCapture();
        var speakers = await builder.BuildAsync();
        try
        {
            return new MeetingCapture(speakers, microphone, captured, microphoneBlocked);
        }
        catch
        {
            speakers.Dispose();
            throw;
        }
    }

    public MicrophoneFailure? Start()
    {
        try
        {
            _microphone.StartRecording();
        }
        catch (MmException e)
        {
            return MicrophoneFailure.FromResult(e.Result);
        }

        try
        {
            _speakers.StartRecording();
        }
        catch
        {
            // Never leave the microphone recording for a meeting that didn't start.
            _stopping = true;
            _microphone.StopRecording();
            throw;
        }

        return null;
    }

    public async Task StopAsync()
    {
        _stopping = true;
        Task microphoneDone;
        lock (_gate)
        {
            _microphone.StopRecording();
            microphoneDone = _microphoneDone.Task;
        }

        _speakers.StopRecording();
        try
        {
            await Task.WhenAll(microphoneDone, _speakersDone.Task).WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogEvent.RecordingStopTimedOut);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        lock (_gate)
        {
            _microphone.Dispose();
        }

        _speakers.Dispose();
    }

    private WaveIn Microphone(int device, TaskCompletionSource done)
    {
        var microphone = new WaveIn
        {
            DeviceNumber = device,
            WaveFormat = AudioRecorder.Format,
            BufferMilliseconds = BufferMilliseconds,
        };
        microphone.DataAvailable += (_, e) =>
        {
            var pcm = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
            _captured(Speaker.You, pcm);
            switch (_watchdog.Check(pcm, System.Diagnostics.Stopwatch.GetElapsedTime(_created)))
            {
                case WatchdogVerdict.Blocked:
                    Log.Warn(LogEvent.MeetingMicrophoneBlocked);
                    _microphoneBlocked(true);
                    ReopenLater();
                    break;
                case WatchdogVerdict.Retry:
                    ReopenLater();
                    break;
                case WatchdogVerdict.Recovered:
                    Log.Info(LogEvent.MeetingMicrophoneRecovered);
                    _microphoneBlocked(false);
                    break;
            }
        };
        microphone.RecordingStopped += (_, e) =>
        {
            done.TrySetResult();
            if (!_stopping && ReferenceEquals(microphone, _microphone))
            {
                // Unplugged: the meeting goes on with the PC's sound until a microphone opens.
                Log.Warn(LogEvent.MeetingMicrophoneStopped);
                _microphoneBlocked(true);
                _ = RetryAsync();
            }
        };
        return microphone;
    }

    // Never from inside the device's own callback, which reopening would dispose.
    private void ReopenLater() => _ = Task.Run(() =>
    {
        try
        {
            Reopen();
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.MeetingMicrophoneReopenFailed, e);
        }
    });

    // Until a microphone opens or the meeting stops; off the UI thread, since opening a
    // Bluetooth microphone can take a while.
    private async Task RetryAsync()
    {
        try
        {
            while (!_stopping)
            {
                await Task.Delay(MicrophoneWatchdog.RetryEvery).ConfigureAwait(false);
                if (Reopen())
                {
                    _microphoneBlocked(false);
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.MeetingMicrophoneReopenFailed, e);
        }
    }

    // Swaps in a fresh microphone on the Windows default; one reopen at a time.
    private bool Reopen()
    {
        if (_stopping || Interlocked.Exchange(ref _reopening, 1) == 1)
        {
            return false;
        }

        try
        {
            lock (_gate)
            {
                if (_stopping)
                {
                    return false;
                }

                var old = _microphone;
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var fresh = Microphone(DefaultDevice, done);
                try
                {
                    fresh.StartRecording();
                }
                catch (MmException e)
                {
                    fresh.Dispose();
                    Log.Warn(LogEvent.MeetingMicrophoneReopenFailed, ("mm_result", (int)e.Result));
                    return false;
                }

                // For a moment both deliver: the old one only silence, which the watchdog already
                // counts as blocked.
                _microphone = fresh;
                _microphoneDone = done;
                old.Dispose();
                Log.Info(LogEvent.MeetingMicrophoneReopened);
                return true;
            }
        }
        finally
        {
            Volatile.Write(ref _reopening, 0);
        }
    }
}
