using Naad.Transcription;

namespace Naad.Live;

/// <summary>
/// One dictation transcribed while it is recorded: on this PC phrase by phrase, or streamed to a
/// cloud service. Its text is user content: shown or typed, never logged.
/// </summary>
internal interface ILiveSession : IDisposable
{
    /// <summary>Everything heard so far, for the overlay. Raised on a thread-pool thread.</summary>
    event Action<string>? TextChanged;

    /// <summary>
    /// The words that won't change any more, from the start, for live typing. Raised on a
    /// thread-pool thread, in order.
    /// </summary>
    event Action<string>? StableChanged;

    /// <summary>
    /// After <see cref="FinishAsync"/>: the live text couldn't be used, so the recording was
    /// transcribed in one pass instead.
    /// </summary>
    bool FellBack { get; }

    /// <summary>The next audio from the microphone. Called on the capture thread.</summary>
    void Add(byte[] pcm);

    /// <summary>
    /// The whole dictation's text, given <paramref name="pcm"/>, the full recording. Whatever went
    /// wrong while live, the recording is transcribed in one pass instead, so no words are lost.
    /// </summary>
    Task<TranscriptionResult> FinishAsync(byte[] pcm, CancellationToken cancellationToken);
}
