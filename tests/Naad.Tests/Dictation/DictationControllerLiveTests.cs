using Naad.Live;
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
        _h.Controller.UseLiveTranscript(() =>
        {
            started++;
            return new LiveTranscript(pieces, SpeechChunker.ForDictation());
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
        _h.Controller.UseLiveTranscript(() => null);

        await _h.DictateAsync(LongHold);

        Assert.Equal(1, _h.Transcriber.Calls);
        Assert.Equal(1, _h.Pastes);
    }

    [Fact]
    public async Task Escape_WhileRecording_DisposesTheLiveTranscript()
    {
        var pieces = new FakeTranscriber();
        LiveTranscript? live = null;
        _h.Controller.UseLiveTranscript(() => live = new LiveTranscript(pieces, SpeechChunker.ForDictation()));
        var idle = _h.NextIdle();

        await _h.Ui.RunAsync(_h.Controller.Press);
        _h.Time.Advance(LongHold);
        await _h.Ui.RunAsync(_h.Controller.Escape);
        await idle.WaitAsync(Harness.Wait, TestContext.Current.CancellationToken);
        live!.Add([.. Live.Audio.Loud(1), .. Live.Audio.Quiet(1)]);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(0, pieces.Calls);
        Assert.Equal(0, _h.Pastes);
    }
}
