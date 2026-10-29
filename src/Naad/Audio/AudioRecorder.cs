using Microsoft.Win32;
using NAudio;
using NAudio.Wave;

namespace Naad.Audio;

/// <summary>
/// Records the chosen microphone, or the Windows default, into memory. Audio never touches the
/// disk. Start, StopAsync and Dispose may be called from different threads; one recording runs
/// at a time.
/// </summary>
internal sealed class AudioRecorder : IAudioRecorder, IDisposable
{
    // 16 kHz mono 16-bit PCM is Whisper's native input, so providers never resample.
    public static readonly WaveFormat Format = new(16000, 16, 1);

    // WAVE_MAPPER: follow the Windows default recording device.
    private const int DefaultDevice = -1;

    // Small buffers so the final words before key release are not cut off.
    private const int BufferMilliseconds = 50;

    // Windows' per-capability privacy switches, under HKLM (device-wide) and HKCU (this user).
    private const string ConsentKey =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    // With 50 ms buffers the last one arrives within milliseconds. A driver that never confirms
    // the stop must not hang dictation, so after this the audio received so far is used.
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private string? _microphone;
    private Recording? _current;
    private bool _disposed;

    /// <summary>
    /// The loudness of each buffer as it arrives: its RMS (0 to 32768) and how much audio it
    /// held. Raised on NAudio's capture thread, about every 50 ms; handlers must be quick and
    /// marshal to the UI thread themselves. Carries a number only, never audio.
    /// </summary>
    public event Action<double, TimeSpan>? LevelMeasured;

    /// <summary>The recording devices' names, in Windows' device order.</summary>
    /// <exception cref="MmException">A device disappeared while the list was read.</exception>
    public static IReadOnlyList<string> DeviceNames() =>
        [.. Enumerable.Range(0, WaveIn.DeviceCount).Select(i => WaveIn.GetCapabilities(i).ProductName)];

    /// <summary>
    /// The device number for <paramref name="microphone"/>, or the Windows default (-1) when it is
    /// null or not connected.
    /// </summary>
    // ponytail: WinMM cuts device names to 31 characters, so two devices that share those characters
    // can't be told apart and the first wins; WASAPI endpoint IDs are the upgrade path.
    public static int DeviceNumber(IReadOnlyList<string> names, string? microphone)
    {
        var index = microphone is null ? -1 : names.ToList().IndexOf(microphone);
        return index < 0 ? DefaultDevice : index;
    }

    /// <summary>Records from <paramref name="microphone"/>, or the default when null, from the next Start.</summary>
    public void UseMicrophone(string? microphone)
    {
        lock (_gate)
        {
            _microphone = microphone;
        }
    }

    /// <summary>Starts recording, or returns why it can't. Returns null once recording.</summary>
    public MicrophoneFailure? Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null)
            {
                throw new InvalidOperationException("Already recording.");
            }

            if (IsBlockedByPrivacySettings())
            {
                return new(MicrophoneError.Blocked);
            }

            if (WaveIn.DeviceCount == 0)
            {
                return new(MicrophoneError.NotFound);
            }

            var recording = new Recording(ChooseDevice(), (rms, duration) => LevelMeasured?.Invoke(rms, duration));
            try
            {
                recording.Start();
            }
            catch (MmException e)
            {
                recording.Dispose();
                return MicrophoneFailure.FromResult(e.Result);
            }

            _current = recording;
            return null;
        }
    }

    /// <summary>
    /// Stops recording and returns the raw PCM in <see cref="Format"/>. Throws if the device
    /// reported an error while recording.
    /// </summary>
    public async Task<byte[]> StopAsync()
    {
        Recording recording;
        lock (_gate)
        {
            recording = _current ?? throw new InvalidOperationException("Not recording.");
            _current = null;
        }

        try
        {
            return await recording.StopAsync();
        }
        finally
        {
            recording.Dispose();
        }
    }

    /// <summary>Closes the device if a recording is running. Later Start calls throw.</summary>
    public void Dispose()
    {
        Recording? recording;
        lock (_gate)
        {
            _disposed = true;
            recording = _current;
            _current = null;
        }

        recording?.Dispose();
    }

    // A chosen microphone that isn't connected falls back to the default rather than failing:
    // the user is holding the shortcut and expects to be heard. Called with _gate held.
    private int ChooseDevice()
    {
        if (_microphone is null)
        {
            return DefaultDevice;
        }

        int device;
        try
        {
            device = DeviceNumber(DeviceNames(), _microphone);
        }
        catch (MmException)
        {
            device = DefaultDevice;
        }

        if (device == DefaultDevice)
        {
            // The device count only; a device's name can identify its owner.
            Log.Warn(LogEvent.MicrophoneFallbackToDefault, ("devices", WaveIn.DeviceCount));
        }

        return device;
    }

    private static bool IsBlockedByPrivacySettings()
    {
        try
        {
            return MicrophoneFailure.IsDeniedByConsent(
            [
                ReadConsent(Registry.LocalMachine, ConsentKey),
                ReadConsent(Registry.CurrentUser, ConsentKey),
                ReadConsent(Registry.CurrentUser, ConsentKey + @"\NonPackaged"),
            ]);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Unreadable consent is not proof of a block; opening the device decides.
            return false;
        }
    }

    private static string? ReadConsent(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue("Value") as string;
    }

    /// <summary>
    /// One recording's device and buffer. Each recording gets its own buffer, so a late buffer
    /// from a stopped or disposed device can never land in another recording, and the buffer
    /// is never disposed under a callback still writing to it.
    /// </summary>
    private sealed class Recording : IDisposable
    {
        private readonly MemoryStream _pcm = new();
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly WaveIn _waveIn;

        public Recording(int device, Action<double, TimeSpan> levelMeasured)
        {
            _waveIn = new()
            {
                DeviceNumber = device,
                WaveFormat = Format,
                BufferMilliseconds = BufferMilliseconds,
            };

            // NAudio raises these on its capture thread.
            _waveIn.DataAvailable += (_, e) =>
            {
                lock (_pcm)
                {
                    _pcm.Write(e.BufferSpan);
                }

                var samples = e.BytesRecorded / Format.BlockAlign;
                levelMeasured(LevelMeter.Rms(e.BufferSpan), TimeSpan.FromSeconds((double)samples / Format.SampleRate));
            };
            _waveIn.RecordingStopped += (_, e) =>
            {
                if (e.Exception is null)
                {
                    _stopped.TrySetResult();
                }
                else
                {
                    _stopped.TrySetException(e.Exception);
                }
            };
        }

        /// <exception cref="MmException">The device could not be opened or started.</exception>
        public void Start() => _waveIn.StartRecording();

        public async Task<byte[]> StopAsync()
        {
            _waveIn.StopRecording();
            try
            {
                // RecordingStopped fires only after the last buffer has been delivered.
                await _stopped.Task.WaitAsync(StopTimeout);
            }
            catch (TimeoutException)
            {
                Log.Warn(LogEvent.RecordingStopTimedOut);
            }

            lock (_pcm)
            {
                return _pcm.ToArray();
            }
        }

        public void Dispose() => _waveIn.Dispose();
    }
}
