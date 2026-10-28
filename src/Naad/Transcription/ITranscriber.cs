namespace Naad.Transcription;

/// <summary>Turns recorded speech into text.</summary>
internal interface ITranscriber
{
    /// <summary>
    /// Transcribes a WAV file. Expected failures (HTTP errors, the network, the timeout, a bad
    /// response) come back as <see cref="TranscriptionResult.Failure"/>, never as exceptions.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was
    /// cancelled. Cancellation is the caller's decision, not a transcription failure.</exception>
    Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken);
}
