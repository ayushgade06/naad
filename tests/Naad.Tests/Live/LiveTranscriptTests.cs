using Naad.Live;
using Naad.Tests.Dictation;
using Naad.Transcription;

namespace Naad.Tests.Live;

[Collection("Log")]
public sealed class LiveTranscriptTests
{
    [Fact]
    public async Task FinishAsync_TwoPhrases_TranscribesTheFirstWhileRecordingAndJoinsBoth()
    {
        var transcriber = Numbered();
        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        var shown = new List<string>();
        live.TextChanged += shown.Add;
        byte[] first = [.. Audio.Loud(1), .. Audio.Quiet(1)];
        byte[] second = Audio.Loud(1);

        live.Add(first);
        live.Add(second);
        var result = await live.FinishAsync([.. first, .. second], TestContext.Current.CancellationToken);

        Assert.Null(result.Failure);
        Assert.Equal("piece 1 piece 2", result.Text);
        Assert.Equal(["piece 1"], shown);
        Assert.Equal(2, transcriber.Calls);
    }

    [Fact]
    public async Task FinishAsync_QuietTail_TranscribesNothingMore()
    {
        var transcriber = Numbered();
        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        byte[] audio = [.. Audio.Loud(1), .. Audio.Quiet(1)];

        live.Add(audio);
        var result = await live.FinishAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal("piece 1", result.Text);
        Assert.Equal(1, transcriber.Calls);
    }

    [Fact]
    public async Task FinishAsync_APieceFailed_TranscribesTheWholeRecordingInOnePass()
    {
        var transcriber = new FakeTranscriber();
        var calls = 0;
        transcriber.Respond = _ => Task.FromResult(++calls == 1
            ? new TranscriptionResult("", new(TranscriptionError.LocalFailed))
            : new TranscriptionResult("whole recording"));
        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        byte[] first = [.. Audio.Loud(1), .. Audio.Quiet(1)];
        byte[] audio = [.. first, .. Audio.Loud(1)];

        live.Add(first);
        var result = await live.FinishAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal("whole recording", result.Text);
        Assert.Equal(audio.Length + 44, transcriber.Uploads[^1].Length);
    }

    [Fact]
    public async Task FinishAsync_NoPause_TranscribesTheRecordingOnce()
    {
        var transcriber = Numbered();
        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        var audio = Audio.Loud(3);

        live.Add(audio);
        var result = await live.FinishAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal("piece 1", result.Text);
        Assert.Equal(audio.Length + 44, Assert.Single(transcriber.Uploads).Length);
    }

    [Fact]
    public async Task Add_AfterDispose_IsIgnored()
    {
        var transcriber = Numbered();
        var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        live.Dispose();

        live.Add([.. Audio.Loud(1), .. Audio.Quiet(1)]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, transcriber.Calls);
    }

    [Fact]
    public async Task Dispose_WhileAPieceIsTranscribed_IgnoresItsText()
    {
        var reply = new TaskCompletionSource<TranscriptionResult>();
        var transcriber = new FakeTranscriber { Respond = _ => reply.Task };
        var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        var shown = 0;
        live.TextChanged += _ => shown++;

        live.Add([.. Audio.Loud(1), .. Audio.Quiet(1)]);
        while (transcriber.Calls == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        live.Dispose();
        reply.SetResult(new("synthetic late piece"));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, shown);
    }

    [Fact]
    public async Task FinishAsync_RecordingShorterThanThePieces_TranscribesItInOnePass()
    {
        var transcriber = Numbered();
        using var live = new LiveTranscript(transcriber, SpeechChunker.ForDictation());
        byte[] audio = [.. Audio.Loud(1), .. Audio.Quiet(1)];

        live.Add(audio);
        var result = await live.FinishAsync(Audio.Loud(1), TestContext.Current.CancellationToken);

        Assert.Equal("piece 2", result.Text.Trim());
        Assert.Equal(Audio.Loud(1).Length + 44, transcriber.Uploads[^1].Length);
    }

    private static FakeTranscriber Numbered()
    {
        var transcriber = new FakeTranscriber();
        var count = 0;
        transcriber.Respond = _ => Task.FromResult(new TranscriptionResult($" piece {++count} "));
        return transcriber;
    }
}
