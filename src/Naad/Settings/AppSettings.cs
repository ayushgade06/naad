using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Naad.Cleanup;
using Naad.Context;
using Naad.History;
using Naad.Hotkey;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Transcription;
using Naad.Window;

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
    public const int CurrentVersion = 4;

    public static AppSettings Defaults { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Write the diagnostic log file. The log never holds user content (see Log).</summary>
    public bool FileLogging { get; init; } = true;

    /// <summary>
    /// Play a short tone when listening starts and stops. Off by default: on PCs whose speakers
    /// sleep, waking them costs a second or more at the start of each dictation (see SoundCues).
    /// </summary>
    public bool PlaySounds { get; init; }

    /// <summary>Also play a low tone when a dictation fails. Follows <see cref="PlaySounds"/>'s reasoning.</summary>
    public bool ErrorSound { get; init; }

    /// <summary>How loud Naad's tones are, 0 to 100.</summary>
    public int SoundVolume { get; init; } = 80;

    /// <summary>Light or dark windows; System follows Windows.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ThemeChoice>))]
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    /// <summary>
    /// Show Naad in the notification area. When hidden, starting Naad again opens its window.
    /// </summary>
    public bool ShowTrayIcon { get; init; } = true;

    /// <summary>The keys to hold while dictating, written like "RightCtrl" (see Chord).</summary>
    public Chord DictationShortcut { get; init; } = Chord.RightCtrl;

    /// <summary>
    /// Whether speech is transcribed by Whisper on this PC or by the cloud API. On this PC by
    /// default: it costs nothing and needs no account.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<TranscriptionProvider>))]
    public TranscriptionProvider TranscriptionProvider { get; init; } = TranscriptionProvider.Local;

    /// <summary>The Whisper model that transcribes on this PC.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WhisperModel>))]
    public WhisperModel LocalModel { get; init; } = WhisperModel.Base;

    /// <summary>The cloud service that transcribes when the provider is Cloud.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CloudService>))]
    public CloudService TranscriptionService { get; init; } = CloudService.Groq;

    /// <summary>
    /// The OpenAI-compatible API root used with the Custom service; "/audio/transcriptions" is
    /// appended to it. A named service uses its own address.
    /// </summary>
    public Uri TranscriptionBaseUrl { get; init; } = new("https://api.groq.com/openai/v1");

    /// <summary>How long one transcription request may take, in seconds.</summary>
    public int TranscriptionTimeoutSeconds { get; init; } = 20;

    /// <summary>The provider's speech-to-text model.</summary>
    public string TranscriptionModel { get; init; } = "whisper-large-v3-turbo";

    /// <summary>The spoken language as an ISO 639-1 code such as "en"; null lets the provider detect it.</summary>
    public string? Language { get; init; }

    /// <summary>The recording device's name as Windows lists it; null follows the Windows default.</summary>
    public string? Microphone { get; init; }

    /// <summary>
    /// The dictionary: names and terms to spell right, one per entry. Sent as Whisper's prompt and
    /// in the cleanup prompt. User content: it goes to the providers but never into the log.
    /// </summary>
    [JsonPropertyName("vocabulary")]
    public IReadOnlyList<string> Words { get; init; } = [];

    /// <summary>The dictionary as one comma-separated line, or null when it is empty.</summary>
    [JsonIgnore]
    public string? Vocabulary => Words.Count == 0 ? null : string.Join(", ", Words);

    /// <summary>
    /// The dictionary as Whisper's prompt: the first words that fit in 500 characters, since
    /// Whisper reads only the start of a long prompt and some services refuse one.
    /// </summary>
    [JsonIgnore]
    public string? TranscriptionPrompt
    {
        get
        {
            var length = -2;
            var words = Words.TakeWhile(w => (length += w.Length + 2) <= MaxVocabularyLength).ToList();
            return words.Count == 0 ? null : string.Join(", ", words);
        }
    }

    /// <summary>How long the history keeps dictations.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<HistoryRetention>))]
    public HistoryRetention HistoryRetention { get; init; } = HistoryRetention.Month;

    /// <summary>A trailing "press enter" is removed and Enter is pressed after the paste.</summary>
    public bool PressEnterCommand { get; init; }

    /// <summary>Keys that paste the last dictation again; null for none.</summary>
    public Chord? PasteAgainShortcut { get; init; }

    /// <summary>Show how long the recording has run in the overlay.</summary>
    public bool OverlayTimer { get; init; }

    /// <summary>
    /// Read the app's name and the text around the cursor when a dictation starts, as spelling
    /// hints for AI cleanup. Never from password fields or the excluded apps.
    /// </summary>
    public bool Context { get; init; } = true;

    /// <summary>Process names, such as "keepass", whose text is never read.</summary>
    public IReadOnlyList<string> ContextExcludedApps { get; init; } = [];

    /// <summary>Send a screenshot of the window to a cloud vision model for spelling hints. Opt-in.</summary>
    public bool ContextScreenshot { get; init; }

    /// <summary>The screenshot's longest side in pixels: 1024, 768, 640 or 512.</summary>
    public int ScreenshotSize { get; init; } = 1024;

    /// <summary>Capture the whole desktop when the window can't be captured on its own.</summary>
    public bool ScreenshotWholeDesktop { get; init; }

    /// <summary>When a dictation over selected text is an instruction to rewrite it.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<EditMode>))]
    public EditMode EditMode { get; init; } = EditMode.Automatic;

    /// <summary>
    /// Transcribe each phrase while still speaking and show it in the overlay. Only with Whisper on
    /// this PC running on a graphics card: on the processor it would slow the dictation down.
    /// </summary>
    public bool LivePreview { get; init; }

    /// <summary>
    /// Type each finished phrase into the window while still speaking. Typed text skips cleanup,
    /// which can't change words already typed. Off by default.
    /// </summary>
    public bool LiveTyping { get; init; }

    /// <summary>Where live preview and typing transcribe: this PC, or a streaming cloud service.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Live.LiveService>))]
    public Live.LiveService LiveService { get; init; } = Live.LiveService.Local;

    /// <summary>The streaming service's model; null uses its default.</summary>
    public string? LiveModel { get; init; }

    /// <summary>
    /// Keep each meeting's recording, encrypted, with its notes. Off by default: the audio is
    /// dropped as soon as each piece is transcribed.
    /// </summary>
    public bool KeepMeetingAudio { get; init; }

    /// <summary>Offer meeting notes when a call app or browser starts using the microphone.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Meetings.MeetingDetection>))]
    public Meetings.MeetingDetection MeetingDetection { get; init; } = Meetings.MeetingDetection.Notify;

    /// <summary>Apps, such as "chrome", that never get a meeting notes offer.</summary>
    public IReadOnlyList<string> MeetingIgnoredApps { get; init; } = [];

    /// <summary>Press the shortcut once to start and again to stop, instead of holding it.</summary>
    public bool HandsFree { get; init; }

    /// <summary>
    /// Whether AI steps run on a language model on this PC or a cloud chat API. On this PC by
    /// default, like transcription: free, offline, and the text never leaves the PC.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LanguageModelProvider>))]
    public LanguageModelProvider LanguageModelProvider { get; init; } = LanguageModelProvider.Local;

    /// <summary>The language model that runs on this PC.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<LocalLanguageModelChoice>))]
    public LocalLanguageModelChoice LocalLanguageModel { get; init; } = LocalLanguageModelChoice.Small;

    /// <summary>The cloud service that runs AI steps when the provider is Cloud.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CloudService>))]
    public CloudService LanguageModelService { get; init; } = CloudService.Groq;

    /// <summary>
    /// The OpenAI-compatible API root used with the Custom service; "/chat/completions" is
    /// appended to it. A named service uses its own address.
    /// </summary>
    public Uri LanguageModelBaseUrl { get; init; } = new("https://api.groq.com/openai/v1");

    /// <summary>
    /// How long cleanup waits for the language model, in seconds; past it the text is pasted as
    /// said. Raise it for a slow server on this PC.
    /// </summary>
    public int LanguageModelTimeoutSeconds { get; init; } = 8;

    /// <summary>The cloud provider's chat model.</summary>
    public string LanguageModelName { get; init; } = "openai/gpt-oss-20b";

    /// <summary>A second cloud model, tried when the first is rate limited or answers badly; null for none.</summary>
    public string? LanguageModelFallbackName { get; init; }

    /// <summary>
    /// How much a transcript is tidied before it is pasted. AI by default; until a language model
    /// is set up, it tidies like Basic.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<CleanupLevel>))]
    public CleanupLevel Cleanup { get; init; } = CleanupLevel.Ai;

    /// <summary>The user's own cleanup instructions; null uses the default. User content: never logged.</summary>
    public string? CleanupPrompt { get; init; }

    /// <summary>
    /// The default prompt's version (<see cref="Naad.Cleanup.CleanupPrompt.DefaultVersion"/>) when
    /// <see cref="CleanupPrompt"/> was written, so a newer default can be offered.
    /// </summary>
    public string? CleanupPromptVersion { get; init; }

    /// <summary>Paste the transcript as said when the model seems to answer it instead of tidying it.</summary>
    public bool InstructionGuard { get; init; } = true;

    /// <summary>A language to write dictations in, such as "German"; null keeps the speaker's.</summary>
    public string? OutputLanguage { get; init; }

    // Model ids are short tokens such as "whisper-large-v3-turbo" or "openai/whisper-1".
    private const int MaxModelLength = 100;

    // A long product or place name fits; a sentence doesn't belong in a dictionary entry.
    public const int MaxWordLength = 100;

    // Whisper reads only the last 224 tokens of a prompt; 500 characters of names and terms
    // stay well inside that.
    private const int MaxVocabularyLength = 500;

    // A request shorter than this can't finish even on a fast connection; longer than five
    // minutes, the user has long given up.
    public const int MinTimeoutSeconds = 2;
    public const int MaxTimeoutSeconds = 300;

    // Room for a long set of instructions, far below what a model's context holds.
    private const int MaxPromptLength = 8000;

    // Language names, in English or their own, are short: "Portuguese (Brazil)" is 19.
    private const int MaxLanguageNameLength = 40;

    // WinMM device names are at most 31 characters (MAXPNAMELEN, 32 with the terminator); a
    // longer name could never match a device.
    private const int MaxMicrophoneLength = 31;

    /// <summary>
    /// An absolute https URL, or http only for a server on this PC: the API key is sent with
    /// every request and must never cross a network in plain text. A user name or password in the
    /// URL is refused: it would sit in settings.json in plain text.
    /// </summary>
    public static bool TryParseBaseUrl(string? text, [NotNullWhen(true)] out Uri? url)
    {
        url = Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && string.IsNullOrEmpty(parsed.Query) && string.IsNullOrEmpty(parsed.Fragment)
            && string.IsNullOrEmpty(parsed.UserInfo)
            && (parsed.Scheme == Uri.UriSchemeHttps || (parsed.Scheme == Uri.UriSchemeHttp && parsed.IsLoopback))
                ? parsed
                : null;
        return url is not null;
    }

    /// <summary>A non-empty token of visible characters, at most 100 long.</summary>
    public static bool IsValidModel(string? model) =>
        model is { Length: > 0 and <= MaxModelLength } && model.All(c => c > ' ' && c < '\u007f');

    /// <summary>
    /// Two or three lowercase ASCII letters: Whisper takes ISO 639-1 codes ("en", "hi"), and a
    /// few providers accept three-letter ones ("haw").
    /// </summary>
    public static bool IsValidLanguage(string? language) =>
        language is { Length: 2 or 3 } && language.All(char.IsAsciiLetterLower);

    /// <summary>1 to 500 characters on one line, without control characters.</summary>
    public static bool IsValidVocabulary(string? vocabulary) =>
        vocabulary is { Length: > 0 and <= MaxVocabularyLength } && !vocabulary.Any(char.IsControl);

    /// <summary>One dictionary entry: 1 to 100 characters, no commas, semicolons or control characters.</summary>
    public static bool IsValidWord(string? word) =>
        word is { Length: > 0 and <= MaxWordLength } && word == word.Trim()
        && !word.Any(c => char.IsControl(c) || c is ',' or ';');

    /// <summary>A screenshot size Naad offers.</summary>
    public static bool IsValidScreenshotSize(int size) => size is 1024 or 768 or 640 or 512;

    /// <summary>
    /// The dictionary entries in <paramref name="text"/>: split on commas, semicolons and new
    /// lines, trimmed, without duplicates in any casing or invalid entries.
    /// </summary>
    public static IReadOnlyList<string> SplitWords(string? text)
    {
        var words = (text ?? "")
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsValidWord)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The shared empty list when there are none, so settings compare equal to the defaults.
        return words.Count == 0 ? (IReadOnlyList<string>)[] : words;
    }

    /// <summary>A volume from 0 to 100.</summary>
    public static bool IsValidVolume(int volume) => volume is >= 0 and <= 100;

    /// <summary>A timeout from 2 to 300 seconds.</summary>
    public static bool IsValidTimeout(int seconds) => seconds is >= MinTimeoutSeconds and <= MaxTimeoutSeconds;

    /// <summary>1 to 8,000 characters; new lines and tabs are the only control characters allowed.</summary>
    public static bool IsValidCleanupPrompt(string? prompt) =>
        prompt is { Length: > 0 and <= MaxPromptLength }
        && !prompt.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t');

    /// <summary>A yyyy-MM-dd date, as <see cref="Naad.Cleanup.CleanupPrompt.DefaultVersion"/> is written.</summary>
    public static bool IsValidPromptVersion(string? version) =>
        DateOnly.TryParseExact(version, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);

    /// <summary>A language name of letters, spaces, hyphens and brackets, up to 40 characters.</summary>
    public static bool IsValidOutputLanguage(string? language) =>
        language is { Length: > 0 and <= MaxLanguageNameLength }
        && language.All(c => char.IsLetter(c) || c is ' ' or '-' or '(' or ')');

    /// <summary>A name WinMM could report: 1 to 31 characters, no control characters.</summary>
    public static bool IsValidMicrophone(string? name) =>
        name is { Length: > 0 and <= MaxMicrophoneLength } && !name.Any(char.IsControl);
}
