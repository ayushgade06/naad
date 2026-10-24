using NAudio.Wave;

namespace Naad.Audio;

/// <summary>Records the default microphone into memory. Audio never touches the disk.</summary>
internal sealed class AudioRecorder : IDisposable
{
    // 16 kHz mono 16-bit PCM is Whisper's native input, so providers never resample.
    public static readonly WaveFormat Format = new(16000, 16, 1);

    // WAVE_MAPPER: follow the Windows default recording device.
    private const int DefaultDevice = -1;

    // Small buffers so the final words before key release are not cut off.
    private const int BufferMilliseconds = 50;

    private readonly MemoryStream _pcm = new();
    private WaveIn? _waveIn;
    private TaskCompletionSource? _stopped;

    /// <exception cref="NAudio.MmException">No microphone, or access is blocked.</exception>
    public void Start()
    {
        if (_waveIn is not null)
        {
            throw new InvalidOperationException("Already recording.");
        }

        lock (_pcm)
        {
            _pcm.SetLength(0);
        }

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waveIn = new WaveIn
        {
            DeviceNumber = DefaultDevice,
            WaveFormat = Format,
            BufferMilliseconds = BufferMilliseconds,
        };
        waveIn.DataAvailable += (_, e) =>
        {
            lock (_pcm)
            {
                _pcm.Write(e.BufferSpan);
            }
        };
        waveIn.RecordingStopped += (_, e) =>
        {
            if (e.Exception is null)
            {
                stopped.TrySetResult();
            }
            else
            {
                stopped.TrySetException(e.Exception);
            }
        };

        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            waveIn.Dispose();
            throw;
        }

        _waveIn = waveIn;
        _stopped = stopped;
    }

    /// <summary>Stops recording and returns the raw PCM in <see cref="Format"/>.</summary>
    public async Task<byte[]> StopAsync()
    {
        var waveIn = _waveIn ?? throw new InvalidOperationException("Not recording.");
        var stopped = _stopped!;
        _waveIn = null;
        _stopped = null;
        try
        {
            waveIn.StopRecording();

            // RecordingStopped fires only after the last buffer has been delivered.
            await stopped.Task;
        }
        finally
        {
            waveIn.Dispose();
        }

        lock (_pcm)
        {
            return _pcm.ToArray();
        }
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _pcm.Dispose();
    }
}
