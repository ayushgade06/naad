namespace Naad.LanguageModel;

/// <summary>Answers one chat prompt: a language model on this PC or a cloud provider.</summary>
internal interface ILanguageModel
{
    /// <summary>
    /// Sends <paramref name="prompt"/> and returns the reply. Expected failures (HTTP errors, the
    /// network, a local model that won't load) come back as <see cref="CompletionResult.Failure"/>,
    /// never as exceptions.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was
    /// cancelled: the caller's time budget ran out, or Naad is shutting down.</exception>
    Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken);
}
