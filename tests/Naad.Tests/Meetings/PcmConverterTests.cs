using System.Buffers.Binary;
using Naad.Meetings;
using NAudio.Wave;

namespace Naad.Tests.Meetings;

public sealed class PcmConverterTests
{
    [Fact]
    public void Convert_48kStereoFloat_Becomes16kMono()
    {
        var converter = new PcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));

        // One second of a constant half-scale signal on both channels, in ten buffers.
        var buffer = new byte[4800 * 2 * 4];
        for (var i = 0; i < 4800 * 2; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i * 4), 0.5f);
        }

        var output = Enumerable.Range(0, 10).SelectMany(_ => converter.Convert(buffer)).ToArray();

        Assert.InRange(output.Length / 2, 15990, 16000);
        Assert.Equal(16384, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(200)));
    }

    [Fact]
    public void Convert_16kMonoPcm_KeepsTheSamplesButHoldsTheLastForTheNextBuffer()
    {
        var converter = new PcmConverter(new WaveFormat(16000, 16, 1));
        var input = new byte[3200];
        BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(100), 1000);

        var output = converter.Convert(input);

        Assert.Equal(1599 * 2, output.Length);
        Assert.Equal(1000, BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(100)));
    }

    [Fact]
    public void New_24BitPcm_IsRefused() =>
        Assert.Throws<NotSupportedException>(() => new PcmConverter(new WaveFormat(48000, 24, 2)));
}
