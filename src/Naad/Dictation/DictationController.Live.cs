using Naad.Live;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>Live transcription: phrases transcribed while the user still speaks.</summary>
internal sealed partial class DictationController
{
    private Func<LiveTranscript?>? _startLive;
    private LiveTranscript? _live;

    /// <summary>
    /// Transcribes each recording piece by piece while it runs, with what <paramref name="start"/>
    /// returns when a recording starts; null, or a null result, transcribes it in one pass on
    /// release. Call on the UI thread.
    /// </summary>
    public void UseLiveTranscript(Func<LiveTranscript?>? start) => _startLive = start;

    // A recording starts: any earlier live transcript is finished with.
    private void StartLive()
    {
        TakeLive()?.Dispose();
        _live = _startLive?.Invoke();
    }

    private LiveTranscript? TakeLive()
    {
        var live = _live;
        _live = null;
        return live;
    }

    // The live transcript's joined pieces, or one pass over the whole recording without one.
    private static Task<TranscriptionResult> TranscribeAsync(
        ITranscriber service, LiveTranscript? live, byte[] pcm, byte[] wav, CancellationToken cancel) =>
        live is null ? service.TranscribeAsync(wav, cancel) : live.FinishAsync(pcm, cancel);
}
