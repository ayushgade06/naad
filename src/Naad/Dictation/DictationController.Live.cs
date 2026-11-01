using Naad.Context;
using Naad.History;
using Naad.Insertion;
using Naad.Live;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>Live transcription: phrases transcribed, and optionally typed, while the user still speaks.</summary>
internal sealed partial class DictationController
{
    private Func<WindowTarget, LiveDictation?>? _startLive;
    private LiveDictation? _live;

    /// <summary>
    /// Live transcription failed, so the recording was transcribed in one pass instead. Raised on
    /// the UI thread.
    /// </summary>
    public event Action? LiveFellBack;

    /// <summary>
    /// Transcribes each recording while it runs, with what <paramref name="start"/> returns for
    /// the window being dictated into when a recording starts; null, or a null result,
    /// transcribes it in one pass on release. Call on the UI thread.
    /// </summary>
    public void UseLiveTranscript(Func<WindowTarget, LiveDictation?>? start) => _startLive = start;

    // A recording starts: any earlier live transcript is finished with.
    private void StartLive()
    {
        TakeLive()?.Dispose();
        _live = _startLive?.Invoke(_target);
        if (_live?.Typer is { } typer)
        {
            _ = AllowTypingAsync(typer, _context);
        }
    }

    private LiveDictation? TakeLive()
    {
        var live = _live;
        _live = null;
        return live;
    }

    // Typing over a selection would replace it, so typing waits until the context read says this
    // isn't an edit. Resumes on the UI thread, where the typer lives.
    private static async Task AllowTypingAsync(LiveTyper typer, Task<DictationContext> context)
    {
        try
        {
            var read = await context;
            typer.Allow(!read.IsEdit && !read.NeedsSelection);
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently. Unknown means no typing.
            Log.Error(LogEvent.LiveTypingFailed, e);
            typer.Allow(false);
        }
    }

    // The live session's text, or one pass over the whole recording without one.
    private static Task<TranscriptionResult> TranscribeAsync(
        ITranscriber service, LiveDictation? live, byte[] pcm, byte[] wav, CancellationToken cancel) =>
        live is null ? service.TranscribeAsync(wav, cancel) : live.Session.FinishAsync(pcm, cancel);

    // Live typing put most of the text in already: the rest is typed as said, without cleanup,
    // which can't change words already typed. If typing stopped early, the full text is kept for
    // Copy last transcript and Paste again instead. Returns the history's outcome.
    private async Task<HistoryOutcome> FinishTypedAsync(LiveTyper typer, string text, CancellationToken cancel)
    {
        await inserter.WaitForModifiersReleasedAsync(TimeLimitReleaseTimeout, cancel);
        _lastText = text;
        if (typer.Finish(text))
        {
            Log.Info(LogEvent.LiveTyped, ("chars", text.Length));
            return HistoryOutcome.Pasted;
        }

        _delivery.KeepLiveTyped(text);
        return HistoryOutcome.Kept;
    }
}
