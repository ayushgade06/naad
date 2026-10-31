namespace Naad.LanguageModel;

/// <summary>
/// Request options that depend on the cloud model. Pure: decided from the model id alone.
/// </summary>
internal static class ModelOptions
{
    /// <summary>
    /// The <c>reasoning_effort</c> to send, or null to send none. Cleanup needs no reasoning, and
    /// a reasoning model left at its default spends seconds thinking first. Models that can't turn
    /// reasoning off get its lowest level; models without reasoning reject the field.
    /// </summary>
    public static string? ReasoningEffort(string model)
    {
        var id = model.ToLowerInvariant();
        return id.Contains("gpt-oss", StringComparison.Ordinal) ? "low"
            : id.Contains("qwen3", StringComparison.Ordinal) ? "none"
            : id.StartsWith("gpt-5", StringComparison.Ordinal) || id.Contains("/gpt-5", StringComparison.Ordinal)
                ? "minimal"
            : id.StartsWith("o1", StringComparison.Ordinal) || id.StartsWith("o3", StringComparison.Ordinal)
                || id.StartsWith("o4", StringComparison.Ordinal) ? "low"
            : id.Contains("grok-3-mini", StringComparison.Ordinal) ? "low"
            : null;
    }

    /// <summary>
    /// Whether the reply limit must be sent as <c>max_completion_tokens</c>: OpenAI's reasoning
    /// models refuse <c>max_tokens</c>, while many servers on a PC know only <c>max_tokens</c>.
    /// </summary>
    public static bool UsesMaxCompletionTokens(string model)
    {
        var id = model.ToLowerInvariant();
        return id.StartsWith("gpt-5", StringComparison.Ordinal) || id.StartsWith("o1", StringComparison.Ordinal)
            || id.StartsWith("o3", StringComparison.Ordinal) || id.StartsWith("o4", StringComparison.Ordinal);
    }
}
