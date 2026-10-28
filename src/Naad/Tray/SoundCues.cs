using System.Buffers.Binary;
using System.Media;
using Naad.Audio;
using Naad.Dictation;

namespace Naad.Tray;

/// <summary>
/// Short tones when listening starts and stops. The tones are synthesized at startup, so there
/// are no sound files and nothing to license. Create, use and dispose on the UI thread.
/// </summary>
internal sealed class SoundCues : IDisposable
{
    // A rising pair: higher when listening starts, lower when it stops, like a tap on and off.
    private const int StartHertz = 880;
    private const int StopHertz = 660;

    // Short and soft: the start tone may reach the microphone, and a 40 ms tone at about
    // -14 dBFS is too brief for Whisper to read as speech.
    private const double ToneSeconds = 0.04;
    private const double Amplitude = 0.2;

    // Fading in and out avoids the click a tone makes when it starts or stops abruptly.
    private const double FadeSeconds = 0.008;

    private readonly SoundPlayer _start = Player(StartHertz);
    private readonly SoundPlayer _stop = Player(StopHertz);
    private DictationState _last;

    /// <summary>Plays the start tone on entering Recording and the stop tone on leaving it.</summary>
    public void OnStateChanged(DictationState state)
    {
        if (state == DictationState.Recording)
        {
            _start.Play();
        }
        else if (_last == DictationState.Recording)
        {
            _stop.Play();
        }

        _last = state;
    }

    /// <summary>A sine tone as a WAV file in the recorder's format.</summary>
    public static byte[] Tone(int hertz)
    {
        var rate = AudioRecorder.Format.SampleRate;
        var samples = (int)(rate * ToneSeconds);
        var fade = (int)(rate * FadeSeconds);
        var pcm = new byte[samples * sizeof(short)];
        for (var i = 0; i < samples; i++)
        {
            var envelope = Math.Min(1.0, Math.Min(i, samples - 1 - i) / (double)fade);
            var value = Amplitude * envelope * Math.Sin(2 * Math.PI * hertz * i / rate);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * sizeof(short)), (short)(value * short.MaxValue));
        }

        return Wav.Encode(pcm, AudioRecorder.Format);
    }

    public void Dispose()
    {
        _start.Dispose();
        _stop.Dispose();
    }

    // SoundPlayer reads the whole stream when loaded, and Play then returns at once.
    private static SoundPlayer Player(int hertz)
    {
        var player = new SoundPlayer(new MemoryStream(Tone(hertz)));
        player.Load();
        return player;
    }
}
