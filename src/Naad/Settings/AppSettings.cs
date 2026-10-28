using System.Diagnostics.CodeAnalysis;
using Naad.Hotkey;

namespace Naad.Settings;

/// <summary>
/// User settings kept in settings.json. Add a property only when the app reads it. Secrets
/// such as API keys, and user content such as transcripts, never belong here.
/// </summary>
internal sealed record AppSettings
{
    /// <summary>
    /// Bump when a property is renamed, removed or changes meaning, and add the matching
    /// migration to <see cref="SettingsStore"/>. Adding a property with a default needs no bump.
    /// </summary>
    public const int CurrentVersion = 1;

    public static AppSettings Defaults { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Write the diagnostic log file. The log never holds user content (see Log).</summary>
    public bool FileLogging { get; init; } = true;

    /// <summary>The keys to hold while dictating, written like "RightCtrl" (see Chord).</summary>
    public Chord DictationShortcut { get; init; } = Chord.RightCtrl;

    /// <summary>The OpenAI-compatible API root; "/audio/transcriptions" is appended to it.</summary>
    public Uri TranscriptionBaseUrl { get; init; } = new("https://api.groq.com/openai/v1");

    /// <summary>The provider's speech-to-text model.</summary>
    public string TranscriptionModel { get; init; } = "whisper-large-v3-turbo";

    // Model ids are short tokens such as "whisper-large-v3-turbo" or "openai/whisper-1".
    private const int MaxModelLength = 100;

    /// <summary>
    /// An absolute https URL, or http only for a server on this PC: the API key is sent with
    /// every request and must never cross a network in plain text.
    /// </summary>
    public static bool TryParseBaseUrl(string? text, [NotNullWhen(true)] out Uri? url)
    {
        url = Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && string.IsNullOrEmpty(parsed.Query) && string.IsNullOrEmpty(parsed.Fragment)
            && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
                ? parsed
                : null;
        return url is not null;
    }

    /// <summary>A non-empty token of visible characters, at most 100 long.</summary>
    public static bool IsValidModel(string? model) =>
        model is { Length: > 0 and <= MaxModelLength } && model.All(c => c > ' ' && c < '\u007f');
}
