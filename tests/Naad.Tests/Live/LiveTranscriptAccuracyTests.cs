using System.Diagnostics;
using Naad.Live;
using Naad.Transcription;

namespace Naad.Tests.Live;

// Needs a downloaded Whisper model and a speech WAV, so it never runs in CI. After dotnet build:
// tests/Naad.Tests/bin/Debug/net10.0-windows/Naad.Tests.exe
// -class Naad.Tests.Live.LiveTranscriptAccuracyTests -explicit only -showLiveOutput
// NAAD_TEST_WAV is a 16 kHz mono 16-bit WAV with a plain 44-byte header, of speech with pauses, such as Windows
// text-to-speech written to a file; NAAD_TEST_MODEL is Base (the default) or Turbo. The clip is
// fed at the speed it was recorded, as the microphone would, then the joined live text is
// compared with one pass over the whole clip. Nothing is uploaded or logged.
[Collection("Log")]
public sealed class LiveTranscriptAccuracyTests
{
    // The tolerance in docs/manual-tests.md (M-LV3): the pieces may differ from one pass by at
    // most this share of words, from words cut at a pause or spelled differently without context.
    private const double MaxWordDifference = 0.15;

    // The microphone's 50 ms buffers.
    private const int BufferBytes = 1600;

    [Fact(Explicit = true)]
    public async Task FinishAsync_RealSpeech_MatchesOnePassWithinTolerance()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var cancel = TestContext.Current.CancellationToken;
        var model = LocalModel.For(
            Enum.Parse<WhisperModel>(Environment.GetEnvironmentVariable("NAAD_TEST_MODEL") ?? "Base"));
        Assert.True(model.IsReady(), "Download the model in Naad's settings first.");
        var wav = File.ReadAllBytes(Environment.GetEnvironmentVariable("NAAD_TEST_WAV")
            ?? throw new InvalidOperationException("Set NAAD_TEST_WAV to a 16 kHz mono WAV of speech."));
        var pcm = wav[44..];
        using var transcriber = new LocalWhisperTranscriber(model.Path, "en", prompt: null);

        // The first run loads the model, as the first dictation does; live preview starts after it.
        await transcriber.TranscribeAsync(wav, cancel);
        var started = Stopwatch.GetTimestamp();
        var single = await transcriber.TranscribeAsync(wav, cancel);
        var singleTime = Stopwatch.GetElapsedTime(started);

        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        var pieces = 0;
        live.TextChanged += _ => Interlocked.Increment(ref pieces);
        for (var i = 0; i < pcm.Length; i += BufferBytes)
        {
            live.Add(pcm[i..Math.Min(i + BufferBytes, pcm.Length)]);
            await Task.Delay(50, cancel);
        }

        started = Stopwatch.GetTimestamp();
        var joined = await live.FinishAsync(pcm, cancel);
        var liveTime = Stopwatch.GetElapsedTime(started);

        var difference = WordDifference(single.Text, joined.Text);
        output.WriteLine($"on graphics card: {transcriber.OnGraphicsCard}, pieces while speaking: {pieces}");
        output.WriteLine($"release to text: one pass {singleTime.TotalMilliseconds:0} ms, " +
            $"live {liveTime.TotalMilliseconds:0} ms; words differing: {difference:P0}");
        Assert.Null(joined.Failure);
        Assert.InRange(difference, 0, MaxWordDifference);
    }

    // Word-level edit distance over the longer text's word count, ignoring case and punctuation.
    private static double WordDifference(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        var row = Enumerable.Range(0, y.Length + 1).ToArray();
        for (var i = 1; i <= x.Length; i++)
        {
            var previous = row[0];
            row[0] = i;
            for (var j = 1; j <= y.Length; j++)
            {
                var current = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (x[i - 1] == y[j - 1] ? 0 : 1));
                previous = current;
            }
        }

        return (double)row[y.Length] / Math.Max(1, Math.Max(x.Length, y.Length));
    }

    private static string[] Words(string text) =>
        new string([.. text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ')])
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
