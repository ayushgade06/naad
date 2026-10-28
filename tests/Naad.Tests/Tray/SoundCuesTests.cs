using System.Buffers.Binary;
using Naad.Audio;
using Naad.Tray;
using NAudio.Wave;

namespace Naad.Tests.Tray;

public sealed class SoundCuesTests
{
    [Theory]
    [InlineData(880)]
    [InlineData(660)]
    public void Tone_IsAValid40MsWavInTheRecorderFormat(int hertz)
    {
        using var reader = new WaveFileReader(new MemoryStream(SoundCues.Tone(hertz)));

        Assert.Equal(AudioRecorder.Format.SampleRate, reader.WaveFormat.SampleRate);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(TimeSpan.FromMilliseconds(40), reader.TotalTime);
    }

    [Fact]
    public void Tone_IsSoftAndFadesInAndOut()
    {
        var tone = SoundCues.Tone(880);
        var samples = Enumerable.Range(0, (tone.Length - Wav.HeaderBytes) / 2)
            .Select(i => BinaryPrimitives.ReadInt16LittleEndian(tone.AsSpan(Wav.HeaderBytes + (i * 2))))
            .ToArray();

        // At most 0.2 of full scale, and silent at both ends so it doesn't click.
        Assert.InRange(samples.Max(s => Math.Abs((int)s)), 1000, 6554);
        Assert.Equal(0, samples[0]);
        Assert.InRange(Math.Abs((int)samples[^1]), 0, 50);
    }
}
