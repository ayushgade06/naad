using System.Buffers.Binary;
using NAudio.Wave;

namespace Naad.Audio;

internal enum RecordingVerdict
{
    /// <summary>Worth transcribing.</summary>
    Accepted,

    /// <summary>Less audio than <see cref="SilenceGate.MinimumAudio"/>.</summary>
    TooShort,

    /// <summary>Too little sound rose above the noise floor to hold a word.</summary>
    Silent,
}

/// <param name="Verdict">Whether to upload the recording.</param>
/// <param name="Duration">How much audio the PCM holds.</param>
/// <param name="Peak">The largest absolute sample, 0 to 32768.</param>
/// <param name="Rms">The RMS level of the loudest 30 ms window, 0 to 32768.</param>
internal readonly record struct RecordingCheck(RecordingVerdict Verdict, TimeSpan Duration, int Peak, int Rms);

/// <summary>
/// Decides whether a recording is worth uploading. Whisper invents text ("Thank you.") for
/// silence and for clips too short to hold a word, so those never leave the machine. Pure: it
/// reads 16-bit mono PCM and nothing else.
/// </summary>
internal static class SilenceGate
{
    /// <summary>
    /// Matches the shortest hold <see cref="Dictation.DictationSession"/> accepts. Checked again on
    /// the audio itself, because the microphone may open late and record less than the hold.
    /// </summary>
    public static readonly TimeSpan MinimumAudio = TimeSpan.FromMilliseconds(300);

    // About -45 dBFS. A muted or privacy-blocked microphone records zeros or a few counts of
    // hiss; quiet speech on a laptop microphone peaks well above -30 dBFS.
    private const int PeakFloor = 180;

    // About -55 dBFS, at or below a quiet room's noise floor. Low on purpose: a silent recording
    // that slips through costs one upload, a quiet sentence thrown away costs the user's words.
    private const int RmsFloor = 60;

    // RMS is measured per 30 ms window, so a short sentence followed by a long pause is not
    // averaged down into "silent". Three loud windows (90 ms) are shorter than any spoken word
    // but longer than a key click or a bump on the desk.
    private const int MinimumLoudWindows = 3;
    private static readonly TimeSpan RmsWindow = TimeSpan.FromMilliseconds(30);

    /// <exception cref="ArgumentException">The format isn't 16-bit mono PCM, or the PCM isn't
    /// a whole number of samples.</exception>
    public static RecordingCheck Check(ReadOnlySpan<byte> pcm, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || format.Channels != 1)
        {
            throw new ArgumentException("Only 16-bit mono PCM can be checked.", nameof(format));
        }

        if (pcm.Length % sizeof(short) != 0)
        {
            throw new ArgumentException("The PCM must hold whole 16-bit samples.", nameof(pcm));
        }

        var samples = pcm.Length / sizeof(short);
        var duration = TimeSpan.FromTicks(samples * TimeSpan.TicksPerSecond / format.SampleRate);
        var window = Math.Max(1, (int)(format.SampleRate * RmsWindow.Ticks / TimeSpan.TicksPerSecond));

        // A trailing partial window is left out of the RMS: a single loud sample there would
        // otherwise count as a loud window. Accepted clips always hold several full windows.
        var peak = 0;
        var loudWindows = 0;
        double loudest = 0;
        double sumOfSquares = 0;
        for (var i = 0; i < samples; i++)
        {
            int sample = BinaryPrimitives.ReadInt16LittleEndian(pcm[(i * sizeof(short))..]);
            peak = Math.Max(peak, Math.Abs(sample));
            sumOfSquares += (double)sample * sample;
            if ((i + 1) % window == 0)
            {
                // Mean squares are compared, not rounded RMS values, so the floor is exact.
                var meanSquare = sumOfSquares / window;
                loudWindows += meanSquare >= (double)RmsFloor * RmsFloor ? 1 : 0;
                loudest = Math.Max(loudest, meanSquare);
                sumOfSquares = 0;
            }
        }

        var verdict = duration < MinimumAudio ? RecordingVerdict.TooShort
            : peak < PeakFloor || loudWindows < MinimumLoudWindows ? RecordingVerdict.Silent
            : RecordingVerdict.Accepted;
        return new(verdict, duration, peak, (int)Math.Round(Math.Sqrt(loudest)));
    }
}
