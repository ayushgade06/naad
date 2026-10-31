using System.Diagnostics;
using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

// The failure tests run everywhere. The explicit test needs a real model (1.1 GB for Small), so
// it never runs in CI. After dotnet build, run it with:
// tests/Naad.Tests/bin/Debug/net10.0-windows/Naad.Tests.exe
// -class Naad.Tests.LanguageModel.LocalLanguageModelTests -explicit only -showLiveOutput
// It downloads the model to Naad's own folder if it isn't there (the same download and checksum
// check as Settings). NAAD_TEST_LLM=Large picks the larger model. To measure the processor
// alone on a PC with a graphics card, set GGML_VK_VISIBLE_DEVICES=99 so Vulkan finds no device.
[Collection("Log")]
public sealed class LocalLanguageModelTests
{
    private static readonly ChatPrompt Prompt = new(
        "You clean up dictated text. Remove filler words, keep only the speaker's final wording, and fix " +
        "punctuation and capitals. Reply with the cleaned text only.",
        "um so i think we should uh meet on tuesday at three no wait four pm to talk about the the budget",
        MaxTokens: 128);

    [Fact]
    public async Task Complete_ModelMissing_FailsAsLocalFailedWithoutThrowing()
    {
        using var model = new LocalLanguageModel(
            Path.Combine(Path.GetTempPath(), $"naad-missing-{Guid.NewGuid():N}.gguf"), TimeProvider.System);

        var result = await model.CompleteAsync(Prompt, CancellationToken.None);

        Assert.Equal(new CompletionResult("", new(CompletionError.LocalFailed)), result);
        Assert.False(model.IsLoaded);
    }

    [Fact]
    public async Task Complete_AlreadyCancelled_Throws()
    {
        using var model = new LocalLanguageModel("unused.gguf", TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.CompleteAsync(Prompt, new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task Complete_AfterDispose_Throws()
    {
        var model = new LocalLanguageModel("unused.gguf", TimeProvider.System);
        model.Dispose();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.CompleteAsync(Prompt, CancellationToken.None));
    }

    [Fact(Explicit = true)]
    public async Task Complete_RealModel_ReportsLatencyMemoryAndUnloadsWhenIdle()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var choice = Environment.GetEnvironmentVariable("NAAD_TEST_LLM") == "Large"
            ? LocalLanguageModelChoice.Large
            : LocalLanguageModelChoice.Small;
        var file = LanguageModelFiles.For(choice);
        if (!file.IsReady())
        {
            using var http = new HttpClient();
            var download = await file.DownloadAsync(http, null, TestContext.Current.CancellationToken);
            Assert.Equal(Naad.Transcription.ModelDownloadOutcome.Downloaded, download.Outcome);
        }

        var idle = new IdleTimer();
        using var model = new LocalLanguageModel(file.Path, idle);
        output.WriteLine($"{choice}: private memory before {PrivateMegabytes()} MB");
        foreach (var run in new[] { "first (loads the model)", "second (model loaded)" })
        {
            var started = Stopwatch.GetTimestamp();
            var result = await model.CompleteAsync(Prompt, TestContext.Current.CancellationToken);
            var wall = Stopwatch.GetElapsedTime(started);

            Assert.Null(result.Failure);
            Assert.NotEmpty(result.Text);
            output.WriteLine($"{run}: {wall.TotalMilliseconds:F0} ms, private memory {PrivateMegabytes()} MB");
            output.WriteLine($"  reply: {result.Text}");
        }

        Assert.Equal(LocalLanguageModel.IdleUnload, idle.Due);
        idle.Fire();
        Assert.False(model.IsLoaded);
        output.WriteLine($"after the idle unload: private memory {PrivateMegabytes()} MB");
    }

    private static long PrivateMegabytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64 / 1_048_576;
    }

    // Holds the idle timer's callback so the test fires it instead of waiting ten minutes.
    private sealed class IdleTimer : TimeProvider
    {
        private TimerCallback? _callback;

        public TimeSpan Due { get; private set; }

        public void Fire() => _callback!(null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _callback = callback;
            return new Timer(this);
        }

        private sealed class Timer(IdleTimer owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                owner.Due = dueTime;
                return true;
            }

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
