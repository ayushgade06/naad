using System.Diagnostics.CodeAnalysis;
using Naad.Cleanup;
using Naad.LanguageModel;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>
/// Checks what the user typed in the settings window and words what the window says back, so
/// the window itself stays a thin layer of controls. Pure: no UI, files or network.
/// </summary>
internal static class SettingsInput
{
    /// <summary>
    /// The settings to save: <paramref name="chosen"/> (shortcut, microphone and switches,
    /// which the window can't get wrong) with the typed fields checked and applied. Text is
    /// trimmed, a blank language means "detect it", a language is lowercased, and a blank
    /// vocabulary sends none. On failure,
    /// <paramref name="error"/> says what to fix and names the field.
    /// </summary>
    public static bool TryBuild(
        AppSettings chosen,
        string baseUrl,
        string model,
        string language,
        string vocabulary,
        [NotNullWhen(true)] out AppSettings? settings,
        [NotNullWhen(false)] out string? error)
    {
        settings = null;
        var languageCode = language.Trim().ToLowerInvariant();
        var terms = vocabulary.Trim();
        error = !AppSettings.TryParseBaseUrl(baseUrl.Trim(), out var url)
                ? "The base URL must start with https:// (or http:// for a server on this PC) and have no " +
                    "query string."
            : !AppSettings.IsValidModel(model.Trim())
                ? "The model must be up to 100 visible characters, without spaces."
            : languageCode.Length > 0 && !AppSettings.IsValidLanguage(languageCode)
                ? "The language must be a two-letter code such as en or hi, or blank to detect it."
            : terms.Length > 0 && !AppSettings.IsValidVocabulary(terms)
                ? "The vocabulary must be one line of up to 500 characters."
            : null;
        if (error is not null)
        {
            return false;
        }

        settings = chosen with
        {
            TranscriptionBaseUrl = url!,
            TranscriptionModel = model.Trim(),
            Language = languageCode.Length == 0 ? null : languageCode,
            Vocabulary = terms.Length == 0 ? null : terms,
        };
        return true;
    }

    /// <summary>
    /// What is wrong with a typed key, or null when it is blank (no change) or usable. Never
    /// repeats the key.
    /// </summary>
    public static string? KeyError(string typed) =>
        typed.Trim() is { Length: > 0 } key && !SecretStore.IsValidKey(key)
            ? "That isn't a valid API key: it has spaces or characters outside plain ASCII."
            : null;

    /// <summary>
    /// The language model fields applied to <paramref name="chosen"/>, checked like the
    /// transcription ones; on failure <paramref name="error"/> names the field.
    /// </summary>
    public static bool TryBuildLanguageModel(
        AppSettings chosen,
        string baseUrl,
        string model,
        [NotNullWhen(true)] out AppSettings? settings,
        [NotNullWhen(false)] out string? error) =>
        TryBuildLanguageModel(chosen, baseUrl, model, "", "", out settings, out error);

    /// <summary>
    /// As above, with the fallback model and the output language, both optional: blank means none.
    /// </summary>
    public static bool TryBuildLanguageModel(
        AppSettings chosen,
        string baseUrl,
        string model,
        string fallbackModel,
        string outputLanguage,
        [NotNullWhen(true)] out AppSettings? settings,
        [NotNullWhen(false)] out string? error)
    {
        settings = null;
        var fallback = fallbackModel.Trim();
        var language = outputLanguage.Trim();
        error = !AppSettings.TryParseBaseUrl(baseUrl.Trim(), out var url)
                ? "The AI base URL must start with https:// (or http:// for a server on this PC) and have no " +
                    "query string."
            : !AppSettings.IsValidModel(model.Trim())
                ? "The AI model must be up to 100 visible characters, without spaces."
            : fallback.Length > 0 && !AppSettings.IsValidModel(fallback)
                ? "The fallback model must be up to 100 visible characters, without spaces."
            : language.Length > 0 && !AppSettings.IsValidOutputLanguage(language)
                ? "The output language must be a language name such as German, or blank."
            : null;
        if (error is not null)
        {
            return false;
        }

        settings = chosen with
        {
            LanguageModelBaseUrl = url!,
            LanguageModelName = model.Trim(),
            LanguageModelFallbackName = fallback.Length == 0 ? null : fallback,
            OutputLanguage = language.Length == 0 ? null : language,
        };
        return true;
    }

    /// <summary>What a prompt test found. Never includes the reply, which the dialog shows on its own.</summary>
    public static string PromptTestMessage(PromptTest test) => test switch
    {
        { Failure: null, Verdict: null } => "No language model is set up: download one or choose the cloud, " +
            "then save, and test again.",
        { Failure: { } failure } => $"The model didn't answer: {CompletionMessage(failure)}",
        { Verdict: ReplyVerdict.Accepted } => $"Replied in {test.Milliseconds} ms. This reply would be pasted.",
        { Verdict: ReplyVerdict.Empty } => $"Replied in {test.Milliseconds} ms: nothing worth pasting, so nothing " +
            "would be pasted.",
        _ => $"Replied in {test.Milliseconds} ms, but the reply looks like a chat answer or is far too long, so " +
            "the text would be pasted as said.",
    };

    /// <summary>A failed request to the language model, in a sentence.</summary>
    public static string CompletionMessage(CompletionFailure failure) => failure.Error switch
    {
        CompletionError.Unauthorized => "the provider rejected the key.",
        CompletionError.RateLimited => "the provider's rate limit was reached.",
        CompletionError.ServerError => "the provider had a problem.",
        CompletionError.Network => "Naad couldn't reach the provider.",
        CompletionError.Timeout => "it took too long.",
        CompletionError.LocalFailed => "the model on this PC failed to run. Download it again if this repeats.",
        CompletionError.MalformedResponse => "the reply wasn't in the expected form.",
        _ => $"the provider refused the request (HTTP {failure.Code}). Check the base URL and model.",
    };

    /// <summary>A model's rate limit, or null when it has none running.</summary>
    public static string? CooldownMessage(string model, Cooldown? cooldown) => cooldown switch
    {
        null => null,
        { Daily: true } => $"Daily limit reached for {model}; it resets at {cooldown.Until.ToLocalTime():g}. " +
            "Until then the fallback model, or the text as said, is used.",
        _ => $"{model} is rate limited until {cooldown.Until.ToLocalTime():t}.",
    };

    /// <summary>Whether the chosen language model is in place, or its download size.</summary>
    public static string LanguageModelStatus(long size, bool ready) => ready
        ? "The model is downloaded. AI steps work offline."
        : $"Running AI on this PC needs a one-time download of {size / 1e9:0.0} GB.";

    /// <summary>Where the transcription key in use comes from. Never shows the key.</summary>
    public static string KeyStatus(ApiKeyResult result) => KeyStatus(result, SecretStore.OverrideVariable);

    /// <summary>Where a key in use comes from, given its override variable. Never shows the key.</summary>
    public static string KeyStatus(ApiKeyResult result, string overrideVariable) =>
        (result.Source, result.Status) switch
        {
            (ApiKeySource.Environment, _) => $"Using the {overrideVariable} environment variable. A key " +
                "saved here is used once that variable is removed.",
            (_, SecretStatus.Loaded) => "A key is stored, encrypted for your Windows account. Paste a new one " +
                "to replace it.",
            (_, SecretStatus.Missing) => "No key is stored. Paste one below; Groq's are at console.groq.com/keys.",
            (_, SecretStatus.Invalid) => $"{overrideVariable} holds characters a key can't have. Fix " +
                "or remove it; a key saved here is used once it is removed.",
            _ => "The stored key can't be read: it may be damaged, or another Windows user saved it. Paste it " +
                "again below.",
        };

    /// <summary>Whether the chosen model for transcribing on this PC is in place, or its download size.</summary>
    public static string ModelStatus(long size, bool ready) => ready
        ? "The model is downloaded. Transcription on this PC works offline."
        : $"Transcribing on this PC needs a one-time download of {(size + 500_000) / 1_000_000} MB.";

    /// <summary>The result of the Download button.</summary>
    public static string ModelDownloadMessage(ModelDownloadResult result) => result.Outcome switch
    {
        // The graphics driver compiles Whisper's shaders on first use and keeps them.
        ModelDownloadOutcome.Downloaded => "Downloaded. The first dictation can take up to a minute while your " +
            "graphics card prepares; after that, about a second.",
        ModelDownloadOutcome.ChecksumMismatch => "The download was damaged, so it was deleted. Try again.",
        ModelDownloadOutcome.HttpError => $"The download server refused the request (HTTP {result.Code}). Try " +
            "again later.",
        ModelDownloadOutcome.Network => "The download failed: check your internet connection and try again.",
        _ => "The model couldn't be saved. Check that the disk has room for it, then try again.",
    };

    /// <summary>The result of the Test key button. Never includes a response body.</summary>
    public static string KeyTestMessage(KeyTestResult result) => result switch
    {
        { Outcome: KeyTestOutcome.Accepted } => "The provider accepted the key.",
        { Outcome: KeyTestOutcome.CannotVerify } => "This provider can't check keys. The key is tried on " +
            "your next dictation.",
        { Failure.Error: TranscriptionError.Unauthorized } => "The provider rejected the key.",
        { Failure.Error: TranscriptionError.RateLimited } => "The provider's rate limit was reached; the key " +
            "itself was not rejected. Try again in a moment.",
        { Failure.Error: TranscriptionError.ServerError } => "The provider had a problem. Try again in a moment.",
        { Failure.Error: TranscriptionError.Network } => "Naad couldn't reach the provider. Check the base " +
            "URL and your internet connection.",
        { Failure.Error: TranscriptionError.Timeout } => "The provider didn't answer in time. Try again.",
        { Failure: { } failure } => $"The provider refused the request (HTTP {failure.Code}). Check the base URL.",
        _ => "The key couldn't be checked.",
    };
}
