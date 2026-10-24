using System.Buffers.Binary;
using Naad.Audio;
using NAudio.Wave;

namespace Naad.Tests.Audio;

public sealed class WavTests
{
    // 0.1 s of a synthetic ramp at the recorder's format; any non-constant bytes will do.
    private static readonly byte[] Pcm = Enumerable.Range(0, 3200).Select(i => (byte)i).ToArray();

    [Fact]
    public void Encode_RecorderFormat_RoundTripsFormatAndSamples()
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
    public void Encode_RecorderFormat_WritesRiffSizeOfWholeFile()
    {
        var wav = Wav.Encode(Pcm, AudioRecorder.Format);

        // Bytes 4-7 hold the file length minus the 8-byte RIFF header. They are only
        // correct if the writer was disposed before the stream was read.
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4, 4)));
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
    }

    [Fact]
    public void Encode_EmptyPcm_ProducesValidEmptyWav()
    {
        var wav = Wav.Encode([], AudioRecorder.Format);

        using var reader = new WaveFileReader(new MemoryStream(wav));
        Assert.Equal(0, reader.Length);
    }
}
