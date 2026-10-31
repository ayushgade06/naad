using System.Buffers.Binary;
using System.Media;
using Naad.Audio;

namespace Naad.Tray;

/// <summary>
/// Short tones when listening starts and stops. The tones are synthesized at startup, so there
/// are no sound files and nothing to license. Create, use and dispose on the UI thread.
/// </summary>
/// <remarks>
/// A tone wakes speakers that have gone idle, which took about 1.3 s on a laptop on battery, and
/// the wake stalls WinMM capture: a tone played while the microphone opened or stopped delayed
/// both by that much, blocked the keyboard hook and lost the first words. So the start tone plays
/// once the microphone is open, and the stop tone once it is closed.
/// ponytail: the start tone still glitches about 1.3 s of capture on such PCs, before it is
/// heard; keeping the speakers awake would prevent idle sleep, so the tones are off by default.
/// </remarks>
internal sealed class SoundCues : IDisposable
{
    // A rising pair: higher when listening starts, lower when it stops, like a tap on and off.
    private const int StartHertz = 880;
    private const int StopHertz = 660;

    // Lower than both: a dictation that failed.
    private const int ErrorHertz = 330;

    // Short and soft: the start tone may reach the microphone, and a 40 ms tone at about
    // -14 dBFS is too brief for Whisper to read as speech.
    private const double ToneSeconds = 0.04;
    private const double Amplitude = 0.2;

    // Fading in and out avoids the click a tone makes when it starts or stops abruptly.
    private const double FadeSeconds = 0.008;

    private readonly SoundPlayer _start;
    private readonly SoundPlayer _stop;
    private readonly SoundPlayer _error;

    /// <param name="volume">0 to 100; 100 is the tones' full, already soft, level.</param>
    public SoundCues(int volume = 100)
    {
        var scale = Math.Clamp(volume, 0, 100) / 100.0;
        _start = Player(StartHertz, scale);
        _stop = Player(StopHertz, scale);
        _error = Player(ErrorHertz, scale);
    }

    /// <summary>Plays the start tone. Call once the microphone is open.</summary>
    public void PlayStart() => _start.Play();

    /// <summary>Plays the stop tone. Call once the microphone is closed.</summary>
    public void PlayStop() => _stop.Play();

    /// <summary>Plays the start tone and returns once it has played, for a preview.</summary>
    public void PreviewStart() => _start.PlaySync();

    /// <summary>Plays the error tone.</summary>
    public void PlayError() => _error.Play();

    /// <summary>A sine tone as a WAV file in the recorder's format.</summary>
    /// <param name="hertz">The pitch.</param>
    /// <param name="scale">0 to 1, applied to the tone's amplitude.</param>
    public static byte[] Tone(int hertz, double scale = 1)
    {
        var rate = AudioRecorder.Format.SampleRate;
        var samples = (int)(rate * ToneSeconds);
        var fade = (int)(rate * FadeSeconds);
        var pcm = new byte[samples * sizeof(short)];
        for (var i = 0; i < samples; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, samples - 1 - i) / (double)fade);
            var value = Amplitude * scale * envelope * Math.Sin(2 * Math.PI * hertz * i / rate);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * sizeof(short)), (short)(value * short.MaxValue));
        }

        return Wav.Encode(pcm, AudioRecorder.Format);
    }

    public void Dispose()
    {
        _start.Dispose();
        _stop.Dispose();
        _error.Dispose();
    }

    // SoundPlayer reads the whole stream when loaded, and Play then returns at once.
    private static SoundPlayer Player(int hertz, double scale)
    {
        var player = new SoundPlayer(new MemoryStream(Tone(hertz, scale)));
        player.Load();
        return player;
    }
}
