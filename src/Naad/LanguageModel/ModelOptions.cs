namespace Naad.LanguageModel;

/// <summary>
/// Request options that depend on the cloud model. Pure: decided from the model id alone.
/// </summary>
internal static class ModelOptions
{
    // Thinking counts against the reply limit; with less room a reasoning model can spend it all
    // before writing a word, even at its lowest effort.
    private const int ReasoningMaxTokens = 4096;

    /// <summary>
    /// Whether to leave out <c>temperature</c> from the first request: OpenAI's reasoning models
    /// refuse any but the default, so leaving it out saves a refused request before the first
    /// cleanup. Other models that refuse it are found by their error and remembered.
    /// </summary>
    public static bool OmitsTemperature(string model) => IsOpenAiReasoning(Id(model));

    /// <summary>
    /// Whether to send <c>include_reasoning: false</c>, so the reasoning isn't sent back before
    /// the reply. Only Groq and OpenRouter take the field, and only for models that reason.
    /// </summary>
    public static bool SendsIncludeReasoning(Uri baseUrl, string model)
    {
        var host = baseUrl.Host.ToLowerInvariant();
        var id = model.ToLowerInvariant();
        return (host is "api.groq.com" or "openrouter.ai")
            && (id.Contains("gpt-oss", StringComparison.Ordinal) || id.Contains("qwen3", StringComparison.Ordinal));
    }

    /// <summary>The reply limit to send for <paramref name="wanted"/> tokens of text.</summary>
    public static int MaxTokens(string model, int wanted) =>
        ReasoningEffort(model) is { } effort && effort != "none" ? Math.Max(wanted, ReasoningMaxTokens) : wanted;

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
    public static bool UsesMaxCompletionTokens(string model) => IsOpenAiReasoning(Id(model));

    // The id without a provider prefix such as "openai/", lowercase.
    private static string Id(string model)
    {
        var id = model.ToLowerInvariant();
        return id[(id.LastIndexOf('/') + 1)..];
    }

    private static bool IsOpenAiReasoning(string id) =>
        id.StartsWith("gpt-5", StringComparison.Ordinal) || id.StartsWith("o1", StringComparison.Ordinal)
        || id.StartsWith("o3", StringComparison.Ordinal) || id.StartsWith("o4", StringComparison.Ordinal);
}
