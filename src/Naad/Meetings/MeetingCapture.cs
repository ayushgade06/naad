using System.Runtime.InteropServices;
using Naad.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Naad.Meetings;

/// <summary>
/// A meeting's two tracks: the microphone (the user) and the PC's sound (everyone else), both
/// through WASAPI. The microphone opens in Windows' communications mode, which cancels the echo of
/// the call coming out of the speakers where the device supports it, and falls back to a plain
/// stream where it doesn't. The PC's sound is a process loopback of every app except Naad, so its
/// own tones are never transcribed as the other side. Both arrive as 16 kHz mono PCM16 on their
/// capture threads. A microphone another app silences, or one that is unplugged, is reopened on the
/// Windows default until it works again. Audio stays in memory. Create with <see cref="CreateAsync"/>.
/// </summary>
internal sealed class MeetingCapture : IMeetingCapture
{
    // Larger buffers than dictation's: nothing waits on the last few milliseconds here.
    private const int BufferMilliseconds = 100;

    // The last buffers arrive within milliseconds of a stop; a driver that never confirms it
    // must not hang the end of a meeting.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private readonly WasapiRecorder _speakers;
    private readonly PcmConverter _converter;
    private readonly MicrophoneWatchdog _watchdog = new();
    private readonly Action<Speaker, byte[]> _captured;
    private readonly Action<bool> _microphoneBlocked;
    private readonly string? _chosen;
    private readonly long _created = System.Diagnostics.Stopwatch.GetTimestamp();
    private readonly TaskCompletionSource _speakersDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WasapiRecorder? _microphone;
    private MMDevice? _endpoint;
    private TaskCompletionSource _microphoneDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stopping;
    private int _reopening;
    private int _blocked;

    private MeetingCapture(
        WasapiRecorder speakers, string? microphone, Action<Speaker, byte[]> captured, Action<bool> microphoneBlocked)
    {
        _speakers = speakers;
        _converter = new(speakers.WaveFormat);
        _captured = captured;
        _microphoneBlocked = microphoneBlocked;
        _chosen = microphone;

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

    /// <summary>Opens the PC's sound, ready to <see cref="Start"/> with the microphone.</summary>
    /// <param name="microphone">The microphone's name as Windows lists it, or null for the Windows default.</param>
    /// <param name="captured">Each buffer of a track, raised on that track's capture thread.</param>
    /// <param name="microphoneBlocked">The microphone went silent or away (true), or works again
    /// (false). Raised on a capture or thread-pool thread, only when that changes.</param>
    /// <exception cref="COMException">The PC's sound can't be captured.</exception>
    /// <exception cref="NotSupportedException">The PC's sound comes in a format Naad can't read.</exception>
    public static async Task<IMeetingCapture> CreateAsync(
        string? microphone, Action<Speaker, byte[]> captured, Action<bool> microphoneBlocked)
    {
        // Process loopback has no mix format of its own, so it is asked for Whisper's directly;
        // the converter still handles whatever arrives. Windows 10 before 2004 lacks it, and
        // captures the whole mix, Naad's tones included.
        var builder = new WasapiRecorderBuilder();
        builder = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
            ? builder.WithProcessLoopback(
                (uint)Environment.ProcessId, ProcessLoopbackMode.ExcludeTargetProcessTree)
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

    // ponytail: opening runs on the UI thread, up to twice (communications, then plain); a slow
    // Bluetooth microphone holds the click that starts notes for that long.
    public MicrophoneFailure? Start()
    {
        lock (_gate)
        {
            if (!Open(_chosen, out var failure))
            {
                return failure;
            }
        }

        try
        {
            _speakers.StartRecording();
        }
        catch
        {
            // Never leave the microphone recording for a meeting that didn't start.
            _stopping = true;
            lock (_gate)
            {
                _microphone?.StopRecording();
            }

            throw;
        }

        return null;
    }

    public async Task StopAsync()
    {
        _stopping = true;
        Task microphoneDone;
        try
        {
            lock (_gate)
            {
                microphoneDone = _microphone is null ? Task.CompletedTask : _microphoneDone.Task;
                _microphone?.StopRecording();
            }
        }
        finally
        {
            // The PC's sound stops even when the microphone refused to.
            _speakers.StopRecording();
        }

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
            _microphone?.Dispose();
            _endpoint?.Dispose();
        }

        _speakers.Dispose();
    }

    // Under _gate. Starts a microphone, echo-cancelled where Windows can and plain otherwise, and
    // makes it the current one before it starts, so its first buffers count; the one before (on a
    // reopen) is the caller's to dispose. False, with the reason, when none opens.
    private bool Open(string? name, out MicrophoneFailure? failure)
    {
        failure = null;
        var (previous, previousEndpoint, previousDone) = (_microphone, _endpoint, _microphoneDone);
        foreach (var communications in new[] { true, false })
        {
            // One per attempt: a failed attempt's stop must not count as the next one's.
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            MMDevice? endpoint = null;
            WasapiRecorder? microphone = null;
            try
            {
                endpoint = Endpoint(name);
                microphone = Microphone(endpoint, communications, done);
                (_microphone, _endpoint, _microphoneDone) = (microphone, endpoint, done);
                microphone.StartRecording();
                Log.Info(
                    LogEvent.MeetingMicrophoneOpened,
                    ("communications", communications ? 1 : 0),
                    ("echo_cancellation", microphone.AcousticEchoCancellationControl is null ? 0 : 1));
                return true;
            }
            catch (Exception e) when (e is COMException or InvalidOperationException or NotSupportedException
                or UnauthorizedAccessException or ArgumentException)
            {
                (_microphone, _endpoint, _microphoneDone) = (previous, previousEndpoint, previousDone);
                microphone?.Dispose();
                endpoint?.Dispose();
                Log.Warn(LogEvent.MeetingMicrophoneOpenFailed, ("communications", communications ? 1 : 0),
                    ("hresult", e.HResult));
                failure = MeetingMicrophone.FromHResult(e.HResult);
            }
        }

        return false;
    }

    // The named microphone, or the Windows default when it is null or not connected. The caller
    // disposes it; the endpoints not chosen are disposed here.
    private static MMDevice Endpoint(string? name)
    {
        using var devices = new MMDeviceEnumerator();
        var connected = devices.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        var index = MeetingMicrophone.Find([.. connected.Select(d => d.FriendlyName)], name);
        for (var i = 0; i < connected.Count; i++)
        {
            if (i != index)
            {
                connected[i].Dispose();
            }
        }

        return index >= 0 ? connected[index] : devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
    }

    private WasapiRecorder Microphone(MMDevice device, bool communications, TaskCompletionSource done)
    {
        var builder = new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithFormat(AudioRecorder.Format)
            .WithBufferLength(BufferMilliseconds);
        var microphone = (communications ? builder.WithCommunicationsMode() : builder).Build();

        // Made at the first buffer, once Windows has settled the format; one capture thread uses it.
        PcmConverter? converter = null;
        microphone.DataAvailable += (buffer, _, _, _) =>
        {
            // While a reopened microphone takes over, the old one's last buffers are dropped, so two
            // devices never mix in one track or share the watchdog.
            if (!ReferenceEquals(microphone, _microphone))
            {
                return;
            }

            converter ??= new(microphone.WaveFormat);
            var pcm = converter.Convert(buffer);
            if (pcm.Length == 0)
            {
                return;
            }

            _captured(Speaker.You, pcm);
            switch (_watchdog.Check(pcm, System.Diagnostics.Stopwatch.GetElapsedTime(_created)))
            {
                case WatchdogVerdict.Blocked:
                    Log.Warn(LogEvent.MeetingMicrophoneBlocked);
                    Blocked(true);
                    ReopenLater();
                    break;
                case WatchdogVerdict.Retry:
                    ReopenLater();
                    break;
                case WatchdogVerdict.Recovered:
                    Log.Info(LogEvent.MeetingMicrophoneRecovered);
                    Blocked(false);
                    break;
            }
        };
        microphone.RecordingStopped += (_, _) =>
        {
            done.TrySetResult();
            if (!_stopping && ReferenceEquals(microphone, _microphone))
            {
                // Unplugged: the meeting goes on with the PC's sound until a microphone opens.
                Log.Warn(LogEvent.MeetingMicrophoneStopped);
                Blocked(true);
                _ = RetryAsync();
            }
        };
        return microphone;
    }

    // The watchdog and an unplugged device can both report; the user hears of a change once.
    private void Blocked(bool blocked)
    {
        if (Interlocked.Exchange(ref _blocked, blocked ? 1 : 0) != (blocked ? 1 : 0))
        {
            _microphoneBlocked(blocked);
        }
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

    // Until a microphone opens (here, or by the watchdog's reopen) or the meeting stops; off the UI
    // thread, since opening a Bluetooth microphone can take a while.
    private async Task RetryAsync()
    {
        var gone = _microphone;
        while (!_stopping && ReferenceEquals(_microphone, gone))
        {
            await Task.Delay(MicrophoneWatchdog.RetryEvery).ConfigureAwait(false);
            try
            {
                if (ReferenceEquals(_microphone, gone) && Reopen())
                {
                    Blocked(false);
                    return;
                }
            }
            catch (Exception e)
            {
                // Logged and tried again: a microphone left dead would lose the rest of the meeting.
                Log.Error(LogEvent.MeetingMicrophoneReopenFailed, e);
            }
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
                var (old, oldEndpoint) = (_microphone, _endpoint);
                if (_stopping || !Open(null, out _))
                {
                    return false;
                }

                old?.Dispose();
                oldEndpoint?.Dispose();
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
