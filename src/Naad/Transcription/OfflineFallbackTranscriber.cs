namespace Naad.Transcription;

/// <summary>
/// Transcribes with the cloud, but on this PC when the PC is offline or the request can't reach
/// the service, so a dictation made without a connection isn't lost. Falling back keeps the audio
/// on the PC, so it never goes against a choice to stay local.
/// </summary>
/// <param name="cloud">The chosen cloud transcriber.</param>
/// <param name="local">Creates the transcriber on this PC, or returns null when its model isn't
/// downloaded. Called only when needed, so the model isn't loaded otherwise; the one it creates is
/// kept, and disposed with this.</param>
/// <param name="isOnline">Whether the PC has a network connection.</param>
/// <param name="fellBack">Called each time this PC transcribed instead; the caller tells the user.</param>
internal sealed class OfflineFallbackTranscriber(
    ITranscriber cloud, Func<ITranscriber?> local, Func<bool> isOnline, Action fellBack) : ITranscriber, IDisposable
{
    private ITranscriber? _local;

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        if (!isOnline() && Local() is { } offline)
        {
            fellBack();
            return await offline.TranscribeAsync(wav, cancellationToken);
        }

        var result = await cloud.TranscribeAsync(wav, cancellationToken);
        if (result.Failure?.Error is TranscriptionError.Network or TranscriptionError.NoInternet
                or TranscriptionError.ConnectionLost
            && Local() is { } fallback)
        {
            fellBack();
            return await fallback.TranscribeAsync(wav, cancellationToken);
        }

        return result;
    }

    public void Dispose() => (_local as IDisposable)?.Dispose();

    private ITranscriber? Local() => _local ??= local();
}
