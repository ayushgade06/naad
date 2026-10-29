using System.Diagnostics;
using Naad.Transcription;

namespace Naad.Tests.Transcription;

// The failure mapping runs everywhere. The explicit test needs the real 148 MB model and a
// speech WAV, so it never runs in CI. After dotnet build, run it with:
// tests/Naad.Tests/bin/Debug/net10.0-windows/Naad.Tests.exe
// -class Naad.Tests.Transcription.LocalWhisperTranscriberTests -explicit only -showLiveOutput
// It downloads the model to Naad's own folder if it isn't there (the same download and checksum
// check as Settings), and reads the WAV from NAAD_TEST_WAV: 16 kHz mono 16-bit, such as Windows
// text-to-speech written to a file. It prints latency and CPU use; nothing is uploaded.
[Collection("Log")]
public sealed class LocalWhisperTranscriberTests
{
    [Fact]
    public async Task Transcribe_ModelMissing_FailsAsLocalFailedWithoutThrowing()
    {
        using var transcriber = new LocalWhisperTranscriber(
            Path.Combine(Path.GetTempPath(), $"naad-missing-{Guid.NewGuid():N}.bin"), "en", prompt: null);

        var result = await transcriber.TranscribeAsync([0x52, 0x49, 0x46, 0x46], CancellationToken.None);

        Assert.Equal(new TranscriptionResult("", new(TranscriptionError.LocalFailed)), result);
    }

    [Fact]
    public async Task Transcribe_AlreadyCancelled_Throws()
    {
        using var transcriber = new LocalWhisperTranscriber("unused.bin", null, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => transcriber.TranscribeAsync([], new CancellationToken(canceled: true)));
    }

    [Fact(Explicit = true)]
    public async Task Transcribe_RealModelAndSpeech_ReportsLatencyAndCpu()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var model = LocalModel.Base;
        if (!model.IsReady())
        {
            using var http = new HttpClient();
            var download = await model.DownloadAsync(http, null, TestContext.Current.CancellationToken);
            Assert.Equal(ModelDownloadOutcome.Downloaded, download.Outcome);
        }

        var wav = File.ReadAllBytes(Environment.GetEnvironmentVariable("NAAD_TEST_WAV")
            ?? throw new InvalidOperationException("Set NAAD_TEST_WAV to a 16 kHz mono WAV of speech."));
        var audioSeconds = (wav.Length - 44) / 32000.0;
        using var transcriber = new LocalWhisperTranscriber(model.Path, "en", prompt: null);
        var process = Process.GetCurrentProcess();

        foreach (var run in new[] { "first (loads the model)", "second (model loaded)" })
        {
            process.Refresh();
            var cpuBefore = process.TotalProcessorTime;
            var started = Stopwatch.GetTimestamp();
            var result = await transcriber.TranscribeAsync(wav, TestContext.Current.CancellationToken);
            var wall = Stopwatch.GetElapsedTime(started);
            process.Refresh();
            var cpu = process.TotalProcessorTime - cpuBefore;

            Assert.Null(result.Failure);
            Assert.NotEmpty(result.Text);
            output.WriteLine(
                $"{run}: audio {audioSeconds:F1} s, wall {wall.TotalMilliseconds:F0} ms, " +
                $"CPU {cpu.TotalMilliseconds:F0} ms ({cpu / wall / Environment.ProcessorCount:P0} of " +
                $"{Environment.ProcessorCount} logical processors), {result.Text.Length} chars");
        }
    }
}
