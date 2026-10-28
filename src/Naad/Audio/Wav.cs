using System.Buffers.Binary;
using NAudio.Wave;

namespace Naad.Audio;

/// <summary>Wraps raw PCM in a canonical 44-byte-header WAV file, in memory.</summary>
internal static class Wav
{
    // RIFF header (12) + "fmt " chunk header (8) + PCM fmt body (16) + "data" chunk header (8).
    public const int HeaderBytes = 44;

    // The RIFF size field counts everything after itself: the file minus "RIFF" and the field.
    private const int RiffPreambleBytes = 8;
    private const int PcmFormatChunkBytes = 16;
    private const ushort PcmFormatTag = 1;

    /// <summary>
    /// Returns a WAV file holding <paramref name="pcm"/>. The PCM is copied once, straight after
    /// the header.
    /// </summary>
    /// <exception cref="ArgumentException">The format isn't byte-aligned integer PCM, the PCM
    /// isn't a whole number of sample frames, or the file would not fit in one array.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> pcm, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.Channels < 1 || format.SampleRate < 1
            || format.BitsPerSample < 8 || format.BitsPerSample % 8 != 0)
        {
            throw new ArgumentException("Only byte-aligned integer PCM can be encoded.", nameof(format));
        }

        // Derived here rather than read from the format, so the header is self-consistent.
        var blockAlign = format.Channels * (format.BitsPerSample / 8);
        if (pcm.Length % blockAlign != 0)
        {
            throw new ArgumentException("The PCM must hold whole sample frames.", nameof(pcm));
        }

        if (pcm.Length > Array.MaxLength - HeaderBytes)
        {
            throw new ArgumentException("The PCM is too long for one WAV array.", nameof(pcm));
        }

        var wav = new byte[HeaderBytes + pcm.Length];
        var header = wav.AsSpan();
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)(wav.Length - RiffPreambleBytes));
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], PcmFormatChunkBytes);
        BinaryPrimitives.WriteUInt16LittleEndian(header[20..], PcmFormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(header[22..], (ushort)format.Channels);
        BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)format.SampleRate);
        BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)((long)format.SampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], (ushort)blockAlign);
        BinaryPrimitives.WriteUInt16LittleEndian(header[34..], (ushort)format.BitsPerSample);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], (uint)pcm.Length);
        pcm.CopyTo(header[HeaderBytes..]);
        return wav;
    }
}
