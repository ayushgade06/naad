using System.Diagnostics;

namespace Naad.Providers;

/// <summary>The cloud services Naad knows. Each keeps one API key, shared by transcription and AI.</summary>
internal enum CloudService
{
    Groq,
    OpenAI,
    Gemini,
    XAI,
    Anthropic,
    Cerebras,
    Deepgram,

    /// <summary>Any OpenAI-compatible endpoint the user names, such as Ollama or LM Studio.</summary>
    Custom,
}

/// <summary>How a service wants its API key sent.</summary>
internal enum KeyHeader
{
    /// <summary><c>Authorization: Bearer KEY</c>, as OpenAI-compatible APIs take it.</summary>
    Bearer,

    /// <summary><c>Authorization: Token KEY</c> (Deepgram).</summary>
    Token,

    /// <summary><c>x-api-key: KEY</c> (Anthropic's own endpoints).</summary>
    ApiKey,

    /// <summary><c>x-goog-api-key: KEY</c> (Gemini's own endpoints).</summary>
    GoogleApiKey,
}

/// <param name="Name">Shown in Settings.</param>
/// <param name="BaseUrl">The OpenAI-compatible API root; null for Custom, which the user names.</param>
/// <param name="TranscriptionModels">Suggested speech models, the default first; empty when the
/// service doesn't transcribe.</param>
/// <param name="LanguageModels">Suggested chat models, the default first and the fallback second;
/// empty when the service has no chat API.</param>
/// <param name="KeyCheck">A request that needs the key and changes nothing: listing models or projects.</param>
/// <param name="KeyCheckHeader">How that request carries the key.</param>
/// <param name="KeysAt">Where to get a key, for Settings to say.</param>
internal sealed record ServicePreset(
    string Name,
    Uri? BaseUrl,
    IReadOnlyList<string> TranscriptionModels,
    IReadOnlyList<string> LanguageModels,
    Uri? KeyCheck,
    KeyHeader KeyCheckHeader,
    string? KeysAt);

/// <summary>
/// What Naad knows about each <see cref="CloudService"/>. Model lists are suggestions: any model
/// id the service offers can be typed instead.
/// </summary>
internal static class CloudServices
{
    public static IReadOnlyList<CloudService> ForTranscription { get; } =
        [CloudService.Groq, CloudService.OpenAI, CloudService.Gemini, CloudService.XAI, CloudService.Deepgram,
            CloudService.Custom];

    public static IReadOnlyList<CloudService> ForLanguageModel { get; } =
        [CloudService.Groq, CloudService.OpenAI, CloudService.Gemini, CloudService.XAI, CloudService.Anthropic,
            CloudService.Cerebras, CloudService.Custom];

    public static ServicePreset For(CloudService service) => service switch
    {
        CloudService.Groq => new(
            "Groq",
            new("https://api.groq.com/openai/v1"),
            ["whisper-large-v3-turbo", "whisper-large-v3"],
            ["openai/gpt-oss-20b", "llama-3.3-70b-versatile", "openai/gpt-oss-120b", "qwen/qwen3-32b"],
            new("https://api.groq.com/openai/v1/models"),
            KeyHeader.Bearer,
            "console.groq.com/keys"),
        CloudService.OpenAI => new(
            "OpenAI",
            new("https://api.openai.com/v1"),
            ["gpt-4o-mini-transcribe", "gpt-4o-transcribe", "whisper-1"],
            ["gpt-4.1-mini", "gpt-4.1-nano", "gpt-5-mini", "gpt-4o-mini"],
            new("https://api.openai.com/v1/models"),
            KeyHeader.Bearer,
            "platform.openai.com/api-keys"),
        CloudService.Gemini => new(
            "Gemini",
            new("https://generativelanguage.googleapis.com/v1beta/openai"),
            ["gemini-2.5-flash", "gemini-2.5-flash-lite"],
            ["gemini-2.5-flash-lite", "gemini-2.5-flash"],
            new("https://generativelanguage.googleapis.com/v1beta/models"),
            KeyHeader.GoogleApiKey,
            "aistudio.google.com/apikey"),
        CloudService.XAI => new(
            "xAI",
            new("https://api.x.ai/v1"),
            ["grok-stt"],
            ["grok-3-mini", "grok-4-fast-non-reasoning"],
            new("https://api.x.ai/v1/models"),
            KeyHeader.Bearer,
            "console.x.ai"),
        CloudService.Anthropic => new(
            "Anthropic",
            new("https://api.anthropic.com/v1"),
            [],
            ["claude-haiku-4-5", "claude-sonnet-4-5"],
            new("https://api.anthropic.com/v1/models"),
            KeyHeader.ApiKey,
            "console.anthropic.com/settings/keys"),
        CloudService.Cerebras => new(
            "Cerebras",
            new("https://api.cerebras.ai/v1"),
            [],
            ["gpt-oss-120b", "llama-3.3-70b"],
            new("https://api.cerebras.ai/v1/models"),
            KeyHeader.Bearer,
            "cloud.cerebras.ai"),
        CloudService.Deepgram => new(
            "Deepgram",
            new("https://api.deepgram.com/v1"),
            ["nova-3", "nova-2"],
            [],
            new("https://api.deepgram.com/v1/projects"),
            KeyHeader.Token,
            "console.deepgram.com"),
        CloudService.Custom => new("Custom (OpenAI-compatible)", null, [], [], null, KeyHeader.Bearer, null),
        _ => throw new UnreachableException(),
    };

    /// <summary>
    /// The service a base URL belongs to, for settings written before services were chosen by
    /// name: a known host picks its service, anything else is Custom.
    /// </summary>
    public static CloudService FromUrl(Uri url) => url.Host.ToLowerInvariant() switch
    {
        "api.groq.com" => CloudService.Groq,
        "api.openai.com" => CloudService.OpenAI,
        "generativelanguage.googleapis.com" => CloudService.Gemini,
        "api.x.ai" => CloudService.XAI,
        "api.anthropic.com" => CloudService.Anthropic,
        "api.cerebras.ai" => CloudService.Cerebras,
        "api.deepgram.com" => CloudService.Deepgram,
        _ => CloudService.Custom,
    };

    /// <summary>The variable that overrides a service's stored key, for development: NAAD_GROQ_API_KEY.</summary>
    public static string OverrideVariable(CloudService service) =>
        $"NAAD_{service.ToString().ToUpperInvariant()}_API_KEY";
}
