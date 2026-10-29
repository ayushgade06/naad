using System.Buffers.Binary;

namespace Naad.Audio;

/// <summary>
/// Turns microphone loudness into the 0 to 1 level the recording overlay draws. Loudness is
/// mapped on a decibel scale, which matches how loud speech sounds, and a falling level decays
/// smoothly instead of jumping, so the bar doesn't flicker between syllables. Pure: it reads
/// numbers and 16-bit mono PCM and nothing else.
/// </summary>
internal sealed class LevelMeter
{
    /// <summary>
    /// The level's zero: about the noise floor of a quiet room, a little below
    /// <see cref="SilenceGate"/>'s RMS floor (about -55 dBFS), so a silent microphone shows an
    /// empty bar and quiet speech still moves it.
    /// </summary>
    public const double FloorDbfs = -60;

    // A full bar falls to empty in about half a second: slow enough to read, fast enough to
    // show that the sound has stopped.
    private const double DecayPerSecond = 2.0;

    // Full scale for 16-bit samples.
    private const double FullScale = 32768;

    /// <summary>The level to draw, 0 (silence) to 1 (full scale).</summary>
    public double Level { get; private set; }

    /// <summary>The RMS of 16-bit mono PCM, 0 to 32768; 0 for no samples.</summary>
    public static double Rms(ReadOnlySpan<byte> pcm)
    {
        var samples = pcm.Length / sizeof(short);
        if (samples == 0)
        {
            return 0;
        }

        double sumOfSquares = 0;
        for (var i = 0; i < samples; i++)
        {
            double sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * sizeof(short))..]);
            sumOfSquares += sample * sample;
        }

        return Math.Sqrt(sumOfSquares / samples);
    }

    /// <summary>
    /// An RMS (0 to 32768) on the meter's scale: <see cref="FloorDbfs"/> and below are 0, full
    /// scale is 1, linear in decibels between them.
    /// </summary>
    public static double Normalize(double rms)
    {
        if (rms <= 0)
        {
            return 0;
        }

        var dbfs = 20 * Math.Log10(rms / FullScale);
        return Math.Clamp((dbfs - FloorDbfs) / -FloorDbfs, 0, 1);
    }

    /// <summary>
    /// Takes the loudness of the audio received over <paramref name="duration"/>. A louder
    /// sound raises the level at once; a quieter one lets it fall by at most the decay rate.
    /// Returns the new level.
    /// </summary>
    public double Add(double rms, TimeSpan duration)
    {
        Level = Math.Max(Normalize(rms), Level - (DecayPerSecond * duration.TotalSeconds));
        return Level;
    }

    /// <summary>Back to silence, for the next recording.</summary>
    public void Reset() => Level = 0;
}
