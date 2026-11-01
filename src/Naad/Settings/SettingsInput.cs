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
    /// which the window can't get wrong) with the typed language and vocabulary checked and
    /// applied (a null vocabulary keeps the dictionary). Text is trimmed, a blank language means "detect it", a
    /// language is lowercased,
    /// and a blank vocabulary sends none. On failure, <paramref name="error"/> says what to fix
    /// and names the field. The cloud fields are checked by <see cref="CloudServiceFields"/>.
    /// </summary>
    public static bool TryBuild(
        AppSettings chosen,
        string language,
        string? vocabulary,
        [NotNullWhen(true)] out AppSettings? settings,
        [NotNullWhen(false)] out string? error)
    {
        settings = null;
        var languageCode = language.Trim().ToLowerInvariant();
        var terms = vocabulary?.Trim() ?? "";
        error = languageCode.Length > 0 && !AppSettings.IsValidLanguage(languageCode)
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
            Language = languageCode.Length == 0 ? null : languageCode,
            Words = vocabulary is null ? chosen.Words : AppSettings.SplitWords(terms),
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

    /// <summary>
    /// The custom endpoints Save checks, each address once: one newly chosen, moved to another
    /// address, or given a new key. One saved before and unchanged isn't asked again.
    /// </summary>
    /// <param name="choices">Each tab's choice, with the service and address saved before.</param>
    public static IReadOnlyList<CloudChoice> EndpointsToCheck(
        IReadOnlyList<(CloudChoice Choice, Providers.CloudService Saved, Uri SavedUrl)> choices)
    {
        var endpoints = new List<CloudChoice>();
        foreach (var (choice, saved, url) in choices)
        {
            if (choice.Service == Providers.CloudService.Custom
                && (saved != Providers.CloudService.Custom || choice.CustomUrl != url || choice.NewKey is not null)
                && !endpoints.Any(e => e.CustomUrl == choice.CustomUrl))
            {
                endpoints.Add(choice);
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Why a custom endpoint can't be saved, or null to save it. Only a definite answer blocks the
    /// save: a rejected key, or an address that isn't an OpenAI-compatible API. One that can't be
    /// reached now may work later, and one that can't list models may still transcribe.
    /// </summary>
    public static string? EndpointSaveError(KeyTestResult result) => result.Failure?.Error switch
    {
        TranscriptionError.Unauthorized => "The custom endpoint rejected the key, so nothing was saved.",
        TranscriptionError.Rejected or TranscriptionError.MalformedResponse =>
            "The custom endpoint didn't answer like an OpenAI-compatible API" +
            (result.Failure.Value.Code > 0 ? $" (HTTP {result.Failure.Value.Code})" : "") +
            ". Check the address; nothing was saved.",
        _ => null,
    };

    /// <summary>What Test context prompt found, in a sentence or two.</summary>
    public static string ContextTestMessage(PromptTest test) => test switch
    {
        { Failure: null, Verdict: null } => "No language model is set up: download one or choose the cloud, " +
            "then save, and test again.",
        { Failure: { } failure } => Context.ContextFailures.Message(Context.ContextFailures.From(failure)),
        { Verdict: ReplyVerdict.Accepted } => $"Replied in {test.Milliseconds} ms. Cleanup would get the first two " +
            "sentences as a hint.",
        _ => $"Replied in {test.Milliseconds} ms, but with nothing usable, so cleanup would go on without it.",
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

    /// <summary>Where a key in use comes from, given its override variable. Never shows the key.</summary>
    public static string KeyStatus(ApiKeyResult result, string overrideVariable) =>
        (result.Source, result.Status) switch
        {
            (ApiKeySource.Environment, _) => $"Using the {overrideVariable} environment variable. A key " +
                "saved here is used once that variable is removed.",
            (_, SecretStatus.Loaded) => "A key is stored, encrypted for your Windows account. Paste a new one " +
                "to replace it.",
            (_, SecretStatus.Missing) => "No key is stored. Paste one below.",
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

    /// <summary>The result of the transcription Test button. Never includes a response body.</summary>
    public static string TranscriptionTestMessage(TranscriptionTest test) => test switch
    {
        { NotSetUp: true } => "Transcription isn't set up yet: add a key, or download the model, and save.",
        { Failure: null } => $"Transcription works: the service answered in {test.Milliseconds} ms.",
        { Failure: { } failure } => Naad.Tray.TrayIcon.TranscriptionErrorMessage(failure.Error),
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
        { Failure.Error: TranscriptionError.NoInternet } => "No internet connection. Connect and try again.",
        { Failure.Error: TranscriptionError.Network } => "Naad couldn't reach the provider. Check the base " +
            "URL and your internet connection.",
        { Failure.Error: TranscriptionError.Timeout } => "The provider didn't answer in time. Try again.",
        { Failure: { } failure } => $"The provider refused the request (HTTP {failure.Code}). Check the base URL.",
        _ => "The key couldn't be checked.",
    };
}
