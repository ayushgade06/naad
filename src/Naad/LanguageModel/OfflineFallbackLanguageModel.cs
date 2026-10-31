namespace Naad.LanguageModel;

/// <summary>
/// Sends prompts to the cloud, but to the model on this PC when the PC is offline or the request
/// can't reach the service. Text then stays on the PC, so this never goes against a choice.
/// </summary>
/// <param name="cloud">The chosen cloud model.</param>
/// <param name="local">Creates the model on this PC, or returns null when it isn't downloaded.
/// Called only when needed; the one it creates is kept, and disposed with this.</param>
/// <param name="isOnline">Whether the PC has a network connection.</param>
/// <param name="fellBack">Called each time this PC answered instead.</param>
internal sealed class OfflineFallbackLanguageModel(
    ILanguageModel cloud, Func<ILanguageModel?> local, Func<bool> isOnline, Action fellBack)
    : ILanguageModel, IDisposable
{
    private ILanguageModel? _local;

    public async Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
    {
        if (!isOnline() && Local() is { } offline)
        {
            fellBack();
            return await offline.CompleteAsync(prompt, cancellationToken);
        }

        var result = await cloud.CompleteAsync(prompt, cancellationToken);
        if (result.Failure?.Error is CompletionError.Network && Local() is { } fallback)
        {
            fellBack();
            return await fallback.CompleteAsync(prompt, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// A dictation started: when offline, the model on this PC starts loading while the user
    /// speaks, so it can answer within the cleanup's time.
    /// </summary>
    public void Prepare()
    {
        if (!isOnline() && Local() is LocalLanguageModel local)
        {
            _ = local.PreloadAsync();
        }
    }

    public void Dispose() => (_local as IDisposable)?.Dispose();

    private ILanguageModel? Local() => _local ??= local();
}
