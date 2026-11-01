using Naad.Live;

using static Naad.Tests.Live.SyntheticAudio;

namespace Naad.Tests.Live;

public sealed class SpeechChunkerTests
{
    // 16 kHz mono PCM16: 32,000 bytes a second.
    private const int BytesPerSecond = 32000;

    [Fact]
    public void Add_SilenceOnly_FindsNoPiece()
    {
        var chunker = SpeechChunker.ForDictation();

        Assert.Empty(chunker.Add(Quiet(5)));
        Assert.Equal(5 * BytesPerSecond, chunker.Cut);
    }

    [Fact]
    public void Add_SpeechThenPause_CutsAtTheEndOfThePause()
    {
        var chunker = SpeechChunker.ForDictation();

        var cuts = chunker.Add([.. Loud(1), .. Quiet(1)]);

        Assert.Equal([(long)(1.6 * BytesPerSecond)], cuts);
    }

    [Fact]
    public void Add_ShortPauseBetweenWords_KeepsOnePiece()
    {
        var chunker = SpeechChunker.ForDictation();

        Assert.Empty(chunker.Add([.. Loud(1), .. Quiet(0.3), .. Loud(1)]));
    }

    [Fact]
    public void Add_BlipThenPause_WaitsToJoinTheNextWords()
    {
        var chunker = SpeechChunker.ForDictation();

        Assert.Empty(chunker.Add([.. Loud(0.2), .. Quiet(1)]));
        Assert.Single(chunker.Add([.. Loud(1), .. Quiet(1)]));
    }

    [Fact]
    public void Add_NoPauseFor20Seconds_CutsAtTheQuietestMomentBefore15()
    {
        var chunker = SpeechChunker.ForDictation();

        // A softer stretch at 11 s, still above the quiet level, is where a word ends.
        var cuts = chunker.Add([.. Loud(11), .. Loud(0.1, 400), .. Loud(9)]);

        Assert.Equal([(long)(11.1 * BytesPerSecond)], cuts);
    }

    [Fact]
    public void Add_PastTheTarget_CutsAtTheFirstQuietWindow()
    {
        var chunker = new SpeechChunker(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(10));

        var cuts = chunker.Add([.. Loud(5), .. Quiet(0.3), .. Loud(6), .. Quiet(0.1)]);

        Assert.Equal([(long)(11.4 * BytesPerSecond)], cuts);
    }

    [Fact]
    public void Add_InOddSizedBuffers_FindsTheSameCuts()
    {
        byte[] audio = [.. Loud(1), .. Quiet(1), .. Loud(2), .. Quiet(1)];
        var whole = SpeechChunker.ForDictation().Add(audio);
        var chunker = SpeechChunker.ForDictation();
        var pieces = new List<long>();

        for (var i = 0; i < audio.Length; i += 1234)
        {
            pieces.AddRange(chunker.Add(audio.AsSpan(i, Math.Min(1234, audio.Length - i))));
        }

        Assert.Equal(2, whole.Count);
        Assert.Equal(whole, pieces);
    }
}

// Synthetic audio for the live tests: a square wave stands in for speech.
internal static class SyntheticAudio
{
    public static byte[] Loud(double seconds, short amplitude = 3000)
    {
        var samples = (int)Math.Round(seconds * 16000);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var value = (short)(i / 20 % 2 == 0 ? amplitude : -amplitude);
            pcm[i * 2] = (byte)value;
            pcm[(i * 2) + 1] = (byte)(value >> 8);
        }

        return pcm;
    }

    public static byte[] Quiet(double seconds) => new byte[(int)Math.Round(seconds * 16000) * 2];
}
