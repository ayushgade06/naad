using Naad.Live;
using Naad.Tests.Live;
using Naad.Transcription;

namespace Naad.Tests.Dictation;

// Live transcription through the real controller.
[Collection("Log")]
public sealed class DictationControllerLiveTests : IDisposable
{
    private static readonly TimeSpan LongHold = TimeSpan.FromSeconds(1);
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Dictate_WithLiveTranscript_UsesItsTextInsteadOfOnePass()
    {
        var pieces = new FakeTranscriber { Respond = _ => Task.FromResult(new TranscriptionResult("synthetic live")) };
        var started = 0;
        _h.Controller.UseLiveTranscript(_ =>
        {
            started++;
            return new(new LiveTranscript(pieces, SpeechChunker.ForDictation()), null);
        });

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, started);
        Assert.Equal(1, pieces.Calls);
        Assert.Equal(0, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_LiveFactoryReturnsNull_TranscribesInOnePass()
    {
        _h.Controller.UseLiveTranscript(_ => null);

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Escape_WhileRecording_DisposesTheLiveTranscript()
    {
        var pieces = new FakeTranscriber();
        LiveTranscript? live = null;
        _h.Controller.UseLiveTranscript(
            _ => new(live = new LiveTranscript(pieces, SpeechChunker.ForDictation()), null));
        var idle = _h.NextIdle();

        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        live!.Add([.. SyntheticAudio.Loud(1), .. SyntheticAudio.Quiet(1)]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, pieces.Calls);
        Assert.Equal(0, _h.Pastes);
    }

    [Fact]
    public async Task Dictate_LiveTypingTypedAPhrase_TypesTheRestAndSkipsCleanup()
    {
        var count = 0;
        var pieces = new FakeTranscriber
        {
            Respond = _ => Task.FromResult(new TranscriptionResult(++count == 1 ? "synthetic one." : "synthetic two.")),
        };
        var typed = new List<string>();
        var cleanup = new FakeCleanup(new("Synthetic cleaned.", Naad.Cleanup.CleanupOutcome.Cleaned));
        _h.Controller.UseCleanup(cleanup);
        LiveTranscript? live = null;
        _h.Controller.UseLiveTranscript(target =>
        {
            live = new LiveTranscript(pieces, SpeechChunker.ForDictation());
            var typer = new LiveTyper(target, _h.CheckTarget, () => false, text =>
            {
                typed.Add(text);
                return Naad.Insertion.TypingResult.Typed;
            });
            live.StableChanged += stable => _h.Ui.Post(_ => typer.Update(stable), null);
            return new(live, typer);
        });
        byte[] first = [.. SyntheticAudio.Loud(1), .. SyntheticAudio.Quiet(1)];
        _h.Recorder.Pcm = [.. first, .. SyntheticAudio.Loud(1)];
        var idle = _h.NextIdle();

        await _h.Ui.RunAsync(_h.Controller.Press);
        live!.Add(first);
        while (typed.Count == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(["synthetic one.", " synthetic two."], typed);
        Assert.Empty(cleanup.Cleaned);
        Assert.Equal(0, _h.Pastes);
        Assert.Empty(_h.Kept);
    }

    [Fact]
    public async Task Dictate_FocusMovedAfterAPhrase_KeepsTheFullText()
    {
        var count = 0;
        var pieces = new FakeTranscriber
        {
            Respond = _ => Task.FromResult(new TranscriptionResult(++count == 1 ? "synthetic one." : "synthetic two.")),
        };
        var typed = new List<string>();
        LiveTranscript? live = null;
        _h.Controller.UseLiveTranscript(target =>
        {
            live = new LiveTranscript(pieces, SpeechChunker.ForDictation());
            var typer = new LiveTyper(target, _h.CheckTarget, () => false, text =>
            {
                typed.Add(text);
                return Naad.Insertion.TypingResult.Typed;
            });
            live.StableChanged += stable => _h.Ui.Post(_ => typer.Update(stable), null);
            return new(live, typer);
        });
        byte[] first = [.. SyntheticAudio.Loud(1), .. SyntheticAudio.Quiet(1)];
        _h.Recorder.Pcm = [.. first, .. SyntheticAudio.Loud(1)];
        var idle = _h.NextIdle();

        await _h.Ui.RunAsync(_h.Controller.Press);
        live!.Add(first);
        while (typed.Count == 0)
        {
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        _h.Decision = Naad.Insertion.InsertionDecision.FocusChanged;
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Release);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);

        Assert.Equal(["synthetic one."], typed);
        Assert.Equal(0, _h.Pastes);
        Assert.Equal([Naad.Dictation.TranscriptKeptReason.LiveTypingStopped], _h.Kept);
    }

    [Fact]
    public async Task Dictate_LiveTypingTypedNothing_CleansUpAndPastesAsUsual()
    {
        var pieces = new FakeTranscriber { Respond = _ => Task.FromResult(new TranscriptionResult("synthetic live")) };
        var typed = new List<string>();
        var cleanup = new FakeCleanup(new("Synthetic cleaned.", Naad.Cleanup.CleanupOutcome.Cleaned));
        _h.Controller.UseCleanup(cleanup);
        _h.Controller.UseLiveTranscript(target => new(
            new LiveTranscript(pieces, SpeechChunker.ForDictation()),
            new LiveTyper(target, _h.CheckTarget, () => false, text =>
            {
                typed.Add(text);
                return Naad.Insertion.TypingResult.Typed;
            })));

        await _h.DictateAsync(LongHold);

        Assert.Empty(typed);
        Assert.Equal(["synthetic live"], cleanup.Cleaned);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Press_WhileBusy_RecordsNothingAndSaysSo()
    {
        var refused = 0;
        _h.Controller.UseBusy(() => true);
        _h.Controller.RefusedWhileBusy += () => refused++;

        await _h.Ui.RunAsync(_h.Controller.Press);

        Assert.Equal(1, refused);
        Assert.Equal(0, _h.Recorder.Starts);
        Assert.Empty(_h.States);
    }
}
