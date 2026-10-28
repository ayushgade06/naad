using System.Buffers.Binary;
using Naad.Audio;
using NAudio.Wave;

namespace Naad.Tests.Audio;

public sealed class SilenceGateTests
{
    // At the recorder's 16 kHz: 4800 samples are 300 ms, 480 samples are one 30 ms window.
    private const int SamplesIn300Ms = 4800;
    private const int SamplesPerWindow = 480;
    private const int SamplesPerSecond = 16000;

    [Fact]
    public void Check_EmptyPcm_IsTooShort()
    {
        var check = SilenceGate.Check([], AudioRecorder.Format);

        Assert.Equal(new RecordingCheck(RecordingVerdict.TooShort, TimeSpan.Zero, 0, 0), check);
    }

    [Theory]
    [InlineData(SamplesIn300Ms - 1, "TooShort")]
    [InlineData(SamplesIn300Ms, "Accepted")]
    public void Check_LoudAudioAround300Ms_RejectsOnlyShorterClips(int samples, string expected)
    {
        var check = SilenceGate.Check(Square(samples, 1000), AudioRecorder.Format);

        Assert.Equal(expected, check.Verdict.ToString());
    }

    [Fact]
    public void Check_OneSecond_ReportsExactDuration()
    {
        var check = SilenceGate.Check(Square(SamplesPerSecond, 1000), AudioRecorder.Format);

        Assert.Equal(TimeSpan.FromSeconds(1), check.Duration);
    }

    [Fact]
    public void Check_DigitalSilence_IsSilent()
    {
        var check = SilenceGate.Check(new byte[SamplesPerSecond * 2], AudioRecorder.Format);

        Assert.Equal(new RecordingCheck(RecordingVerdict.Silent, TimeSpan.FromSeconds(1), 0, 0), check);
    }

    [Fact]
    public void Check_FaintHiss_IsSilent()
    {
        var check = SilenceGate.Check(Square(SamplesPerSecond, 20), AudioRecorder.Format);

        Assert.Equal(RecordingVerdict.Silent, check.Verdict);
        Assert.Equal(20, check.Peak);
        Assert.Equal(20, check.Rms);
    }

    // A square wave's peak and RMS both equal its amplitude, so this isolates the peak floor (180).
    [Theory]
    [InlineData(179, "Silent")]
    [InlineData(180, "Accepted")]
    public void Check_AmplitudeAroundPeakFloor_AcceptsFromTheFloor(short amplitude, string expected)
    {
        var check = SilenceGate.Check(Square(SamplesPerSecond, amplitude), AudioRecorder.Format);

        Assert.Equal(expected, check.Verdict.ToString());
    }

    // One pulse every 16 samples is 30 per window, so RMS = amplitude * sqrt(30 / 480) = amplitude / 4.
    // 240 gives exactly 60, the RMS floor; 239 gives 59.75, which must not be rounded up to pass.
    [Theory]
    [InlineData(239, "Silent")]
    [InlineData(240, "Accepted")]
    public void Check_SparsePulsesAroundRmsFloor_AcceptsFromTheFloor(short amplitude, string expected)
    {
        var pcm = Samples(SamplesPerSecond, i => i % 16 == 0 ? amplitude : (short)0);

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(expected, check.Verdict.ToString());
        Assert.Equal(60, check.Rms);
    }

    // A word needs at least three loud 30 ms windows; two (60 ms) are a click or a bump.
    [Theory]
    [InlineData(2, "Silent")]
    [InlineData(3, "Accepted")]
    public void Check_LoudWindowsInSilence_NeedsThree(int loudWindows, string expected)
    {
        // The burst starts at sample 4800, a window boundary, so it fills whole windows.
        var start = SamplesIn300Ms;
        var end = start + (loudWindows * SamplesPerWindow);
        var pcm = Samples(SamplesPerSecond, i => i >= start && i < end ? (short)(i % 2 == 0 ? 1000 : -1000) : (short)0);

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(expected, check.Verdict.ToString());
    }

    [Fact]
    public void Check_SingleClickInSilence_IsSilent()
    {
        var pcm = Samples(SamplesPerSecond, i => i == 8000 ? (short)30000 : (short)0);

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(RecordingVerdict.Silent, check.Verdict);
        Assert.Equal(30000, check.Peak);
    }

    [Fact]
    public void Check_LoudTrailingPartialWindow_IsIgnoredForRms()
    {
        // Ten silent full windows, then 100 loud samples that never complete a window.
        var pcm = Samples(SamplesIn300Ms + 100, i => i >= SamplesIn300Ms ? (short)1000 : (short)0);

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(RecordingVerdict.Silent, check.Verdict);
        Assert.Equal(0, check.Rms);
    }

    [Fact]
    public void Check_QuietSpeechLikeTone_IsAccepted()
    {
        // About -30 dBFS: a 200 Hz tone with amplitude 1000, RMS about 707.
        var pcm = Samples(SamplesPerSecond, i => (short)Math.Round(1000 * Math.Sin(2 * Math.PI * 200 * i / 16000.0)));

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(RecordingVerdict.Accepted, check.Verdict);
        Assert.InRange(check.Rms, 700, 714);
    }

    [Fact]
    public void Check_ShortSentenceThenTwoMinutesOfSilence_IsAccepted()
    {
        // Averaged over the whole clip, half a second at RMS 1000 in 120.5 s is about RMS 64, near the
        // floor; per window it stays at 1000.
        var speech = SamplesPerSecond / 2;
        var pcm = Samples(
            speech + (120 * SamplesPerSecond), i => i < speech ? (short)(i % 2 == 0 ? 1000 : -1000) : (short)0);

        var check = SilenceGate.Check(pcm, AudioRecorder.Format);

        Assert.Equal(RecordingVerdict.Accepted, check.Verdict);
        Assert.Equal(1000, check.Rms);
    }

    [Fact]
    public void Check_FullScaleNegativeSample_ReportsPeak32768()
    {
        var pcm = Samples(SamplesPerSecond, i => i == 0 ? short.MinValue : (short)0);

        Assert.Equal(32768, SilenceGate.Check(pcm, AudioRecorder.Format).Peak);
    }

    [Fact]
    public void Check_OddByteCount_Throws() =>
        Assert.Throws<ArgumentException>(() => SilenceGate.Check(new byte[3], AudioRecorder.Format));

    [Theory]
    [InlineData(16, 2)]
    [InlineData(8, 1)]
    public void Check_FormatOtherThan16BitMono_Throws(int bits, int channels) =>
        Assert.Throws<ArgumentException>(
            () => SilenceGate.Check(new byte[64], new WaveFormat(16000, bits, channels)));

    private static byte[] Square(int samples, short amplitude) =>
        Samples(samples, i => i % 2 == 0 ? amplitude : (short)-amplitude);

    private static byte[] Samples(int count, Func<int, short> sample)
    {
        var pcm = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), sample(i));
        }

        return pcm;
    }
}
