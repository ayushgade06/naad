using System.Buffers.Binary;
using Naad.Audio;

namespace Naad.Tests.Audio;

public sealed class LevelMeterTests
{
    private static readonly TimeSpan Buffer = TimeSpan.FromMilliseconds(50);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(-5, 0.0)]
    // -60 dBFS (the floor) and below are empty.
    [InlineData(32.768, 0.0)]
    [InlineData(10, 0.0)]
    // -30 dBFS is half way.
    [InlineData(1036.2, 0.5)]
    // Full scale and above are full.
    [InlineData(32768, 1.0)]
    [InlineData(40000, 1.0)]
    public void Normalize_Rms_MapsDecibelsLinearly(double rms, double expected)
    {
        Assert.Equal(expected, LevelMeter.Normalize(rms), precision: 3);
    }

    [Fact]
    public void Normalize_LouderIsNeverLower()
    {
        var levels = Enumerable.Range(0, 33).Select(i => LevelMeter.Normalize(i * 1000)).ToList();

        Assert.Equal(levels.Order(), levels);
    }

    [Theory]
    [InlineData(new short[0], 0)]
    [InlineData(new short[] { 0, 0, 0, 0 }, 0)]
    [InlineData(new short[] { 1000, -1000, 1000, -1000 }, 1000)]
    [InlineData(new short[] { 3, 4 }, 3.5355)]
    [InlineData(new short[] { short.MinValue, short.MinValue }, 32768)]
    public void Rms_Samples_IsRootMeanSquare(short[] samples, double expected)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), samples[i]);
        }

        Assert.Equal(expected, LevelMeter.Rms(pcm), precision: 3);
    }

    [Fact]
    public void Add_LouderSound_RaisesAtOnce()
    {
        var meter = new LevelMeter();

        Assert.Equal(1.0, meter.Add(32768, Buffer), precision: 3);
    }

    [Fact]
    public void Add_Silence_DecaysGraduallyToZero()
    {
        var meter = new LevelMeter();
        meter.Add(32768, Buffer);

        var falling = Enumerable.Range(0, 12).Select(_ => meter.Add(0, Buffer)).ToList();

        Assert.Equal(0.9, falling[0], precision: 3);
        Assert.Equal(falling.OrderDescending(), falling);
        Assert.Equal(0.0, falling[^1], precision: 3);
    }

    [Fact]
    public void Add_QuieterSoundAboveDecay_FallsOnlyToThatSound()
    {
        var meter = new LevelMeter();
        meter.Add(32768, Buffer);

        // Half a second of -30 dBFS: the level decays to 0.5 and stays there.
        for (var i = 0; i < 10; i++)
        {
            meter.Add(1036.2, Buffer);
        }

        Assert.Equal(0.5, meter.Level, precision: 3);
    }

    [Fact]
    public void Reset_AfterSound_IsSilent()
    {
        var meter = new LevelMeter();
        meter.Add(32768, Buffer);

        meter.Reset();

        Assert.Equal(0, meter.Level);
    }
}
