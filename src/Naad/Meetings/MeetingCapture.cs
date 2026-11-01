using Naad.Audio;
using NAudio;
using NAudio.Wave;

namespace Naad.Meetings;

/// <summary>
/// A meeting's two tracks: the microphone (the user) through WinMM, and the PC's sound (everyone
/// else) through WASAPI process loopback of every app except Naad, so its own tones are never
/// transcribed as the other side. Both arrive as 16 kHz mono PCM16 on their capture threads.
/// Audio stays in memory. Create with <see cref="CreateAsync"/>.
/// </summary>
internal sealed class MeetingCapture : IMeetingCapture
{
    // Larger buffers than dictation's: nothing waits on the last few milliseconds here.
    private const int BufferMilliseconds = 100;

    // The last buffers arrive within milliseconds of a stop; a driver that never confirms it
    // must not hang the end of a meeting.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly WaveIn _microphone;
    private readonly WasapiRecorder _speakers;
    private readonly PcmConverter _converter;
    private readonly TaskCompletionSource _microphoneDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _speakersDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _stopping;

    private MeetingCapture(
        WasapiRecorder speakers, int microphone, Action<Speaker, byte[]> captured, Action microphoneLost)
    {
        _microphone = new()
        {
            DeviceNumber = microphone,
            WaveFormat = AudioRecorder.Format,
            BufferMilliseconds = BufferMilliseconds,
        };
        _speakers = speakers;
        _converter = new(speakers.WaveFormat);
        _microphone.DataAvailable += (_, e) => captured(Speaker.You, e.Buffer.AsSpan(0, e.BytesRecorded).ToArray());
        _microphone.RecordingStopped += (_, e) =>
        {
            _microphoneDone.TrySetResult();
            if (!_stopping)
            {
                Log.Warn(LogEvent.MeetingMicrophoneStopped, ("error", e.Exception is null ? 0 : 1));
                microphoneLost();
            }
        };

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
    /// <param name="microphoneLost">The microphone stopped by itself, as when it is unplugged.</param>
    /// <exception cref="System.Runtime.InteropServices.COMException">The PC's sound can't be captured.</exception>
    /// <exception cref="NotSupportedException">The PC's sound comes in a format Naad can't read.</exception>
    public static async Task<IMeetingCapture> CreateAsync(
        int microphone, Action<Speaker, byte[]> captured, Action microphoneLost)
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
            return new MeetingCapture(speakers, microphone, captured, microphoneLost);
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
        _microphone.StopRecording();
        _speakers.StopRecording();
        try
        {
            await Task.WhenAll(_microphoneDone.Task, _speakersDone.Task).WaitAsync(StopTimeout);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogEvent.RecordingStopTimedOut);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _microphone.Dispose();
        _speakers.Dispose();
    }
}
