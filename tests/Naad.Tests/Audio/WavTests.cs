using System.Buffers.Binary;
using Naad.Audio;
using NAudio.Wave;

namespace Naad.Tests.Audio;

public sealed class WavTests
{
    // 0.1 s of a synthetic ramp at the recorder's format; any non-constant bytes will do.
    private static readonly byte[] Pcm = Enumerable.Range(0, 3200).Select(i => (byte)i).ToArray();

    [Fact]
    public void Encode_TwoSamples_MatchesFixture()
    {
        // Samples 1 and 32767 at 16 kHz mono 16-bit, written out by hand from the WAV spec.
        byte[] expected =
        [
            .."RIFF"u8, 0x28, 0x00, 0x00, 0x00, .."WAVE"u8, // RIFF size 40 = 48 - 8
            .."fmt "u8, 0x10, 0x00, 0x00, 0x00,              // fmt chunk of 16 bytes
            0x01, 0x00,                                      // format tag 1: integer PCM
            0x01, 0x00,                                      // 1 channel
            0x80, 0x3E, 0x00, 0x00,                          // 16000 samples per second
            0x00, 0x7D, 0x00, 0x00,                          // 32000 bytes per second
            0x02, 0x00,                                      // 2 bytes per sample frame
            0x10, 0x00,                                      // 16 bits per sample
            .."data"u8, 0x04, 0x00, 0x00, 0x00,              // 4 bytes of samples
            0x01, 0x00, 0xFF, 0x7F,
        ];

        var wav = Wav.Encode([0x01, 0x00, 0xFF, 0x7F], AudioRecorder.Format);

        Assert.Equal(expected, wav);
    }

    [Fact]
    public void Encode_RecorderFormat_RoundTripsThroughNAudioReader()
    {
        var wav = Wav.Encode(Pcm, AudioRecorder.Format);

        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        var samples = new byte[Pcm.Length];
        Assert.Equal(Pcm.Length, reader.Read(samples, 0, samples.Length));
        Assert.Equal(Pcm, samples);
    }

    [Fact]
    public void Encode_OneSecondAtRecorderFormat_WritesRiffAndDataSizes()
    {
        var wav = Wav.Encode(new byte[32000], AudioRecorder.Format);

        Assert.Equal(32044, wav.Length);
        Assert.Equal(32036u, ReadUInt32(wav, 4));
        Assert.Equal(32000u, ReadUInt32(wav, 40));
    }

    [Theory]
    [InlineData(44100, 16, 2, 176400u, 4)]
    [InlineData(8000, 8, 1, 8000u, 1)]
    [InlineData(48000, 24, 2, 288000u, 6)]
    public void Encode_OtherFormats_WritesFormatFields(
        int sampleRate, int bits, int channels, uint byteRate, ushort blockAlign)
    {
        var wav = Wav.Encode(new byte[blockAlign * 3], new WaveFormat(sampleRate, bits, channels));

        Assert.Equal((ushort)channels, ReadUInt16(wav, 22));
        Assert.Equal((uint)sampleRate, ReadUInt32(wav, 24));
        Assert.Equal(byteRate, ReadUInt32(wav, 28));
        Assert.Equal(blockAlign, ReadUInt16(wav, 32));
        Assert.Equal((ushort)bits, ReadUInt16(wav, 34));
        Assert.Equal((uint)(blockAlign * 3), ReadUInt32(wav, 40));
    }

    [Fact]
    public void Encode_EmptyPcm_WritesHeaderOnly()
    {
        var wav = Wav.Encode([], AudioRecorder.Format);

        Assert.Equal(44, wav.Length);
        Assert.Equal(36u, ReadUInt32(wav, 4));
        Assert.Equal(0u, ReadUInt32(wav, 40));
        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.Equal(0, reader.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Encode_PartialSample_Throws(int bytes) =>
        Assert.Throws<ArgumentException>(() => Wav.Encode(new byte[bytes], AudioRecorder.Format));

    [Fact]
    public void Encode_StereoWithHalfAFrame_Throws() =>
        Assert.Throws<ArgumentException>(() => Wav.Encode(new byte[6], new WaveFormat(16000, 16, 2)));

    [Fact]
    public void Encode_FloatFormat_Throws() =>
        Assert.Throws<ArgumentException>(
            () => Wav.Encode(new byte[8], WaveFormat.CreateIeeeFloatWaveFormat(16000, 1)));

    private static uint ReadUInt32(byte[] wav, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(offset));

    private static ushort ReadUInt16(byte[] wav, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(offset));
}
