using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Naad.Hotkey;
using Naad.Providers;
using Naad.Transcription;
using static Naad.Settings.SettingsFields;

namespace Naad.Settings;

/// <summary>What <see cref="SettingsStore.Load"/> found in the settings file.</summary>
internal enum SettingsStatus
{
    Loaded,
    Missing,
    Migrated,
    Repaired,
    Corrupt,
    FutureVersion,
    ReadFailed,
}

/// <param name="Settings">The settings to use; always valid.</param>
/// <param name="Status">What was found, for the caller to log.</param>
/// <param name="FileVersion">The schema version found in the file, when it had a readable one.</param>
/// <param name="Saved">Whether settings.json was written (created, migrated, repaired or replaced).</param>
/// <param name="Error">The exception behind a failed read or write, for logging its type.</param>
internal sealed record SettingsLoadResult(
    AppSettings Settings,
    SettingsStatus Status,
    int FileVersion = 0,
    bool Saved = false,
    Exception? Error = null);

/// <summary>
/// Reads settings.json. Every failure ends in usable settings: a missing, empty, malformed,
/// unreadable or newer-version file yields defaults, and a wrong-typed value falls back to its
/// default. A missing file is created with defaults. A migrated, repaired or corrupt file is
/// first copied to settings.json.&lt;reason&gt;-&lt;UTC time&gt;.bak and then rewritten. A newer-version
/// file is never written. Nothing here logs; the caller logs the returned status.
/// </summary>
internal sealed class SettingsStore(string path)
{
    // Hand-edited files may differ in casing, keep comments, or leave a trailing comma.
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,

        // The default encoder writes "+" as +, which breaks shortcuts like
        // "LeftCtrl+Space" for anyone editing the file. It is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Migrations[n] upgrades a version-n file to version n + 1, in place.
    private static readonly Action<JsonObject>[] Migrations =
    [
        // 0 -> 1: files written before the version field existed. Their fields already
        // mean what version 1 means, so only the version changes.
        _ => { },

        // 1 -> 2: transcribing on this PC became the default. A version-1 file without the
        // field was using the cloud, so it keeps the cloud instead of changing provider unasked.
        root => root.TryAdd("transcriptionProvider", nameof(TranscriptionProvider.Cloud)),

        // 2 -> 3: cloud services are chosen by name; the base URL is only for a custom endpoint.
        // A version-2 file names its service through its URL, so the URL picks it.
        root =>
        {
            root.TryAdd("transcriptionService", ServiceFromUrl(root, "transcriptionBaseUrl"));
            root.TryAdd("languageModelService", ServiceFromUrl(root, "languageModelBaseUrl"));
        },

        // 3 -> 4: the vocabulary, one comma-separated line, becomes a list of dictionary entries.
        root =>
        {
            if (root["vocabulary"] is JsonValue value && value.TryGetValue(out string? text))
            {
                // A long run without commas was words separated by spaces.
                var pieces = (text ?? "").Split([',', ';', '\n', '\r'])
                    .SelectMany(p => p.Trim().Length > AppSettings.MaxWordLength ? p.Split(' ') : [p]);
                var words = AppSettings.SplitWords(string.Join(',', pieces));
                root["vocabulary"] = new JsonArray([.. words.Select(w => (JsonNode)w)]);
            }
        },

        // 4 -> 5: one shortcut, held or (with handsFree) pressed, becomes a shortcut per role. The
        // old one keeps its role; the other dictation role is off, as it was.
        root =>
        {
            if (!root.ContainsKey("dictationShortcut") && !root.ContainsKey("handsFree"))
            {
                return;
            }

            // An unreadable shortcut was Right Ctrl in version 4 too; it keeps its role here.
            var shortcut = root["dictationShortcut"] is JsonValue old && old.TryGetValue(out string? text)
                && Chord.TryParse(text, out _)
                ? old.DeepClone()
                : JsonValue.Create(Chord.RightCtrl.ToString());
            var handsFree = root["handsFree"] is JsonValue value && value.TryGetValue(out bool on) && on;
            root.Remove("dictationShortcut");
            root.Remove("handsFree");
            root.TryAdd("holdShortcut", handsFree ? null : shortcut);
            root.TryAdd("toggleShortcut", handsFree ? shortcut : null);
        },
    ];

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Naad", "settings.json");

    // A missing or invalid URL meant the default, Groq.
    private static string ServiceFromUrl(JsonObject root, string name) =>
        (root.TryGetPropertyValue(name, out var node) && node is JsonValue value
            && value.TryGetValue(out string? text) && AppSettings.TryParseBaseUrl(text, out var url)
                ? CloudServices.FromUrl(url)
                : CloudService.Groq).ToString();

    internal static int MigrationCount => Migrations.Length;

    public SettingsLoadResult Load()
    {
        string json;
        try
        {
            if (!File.Exists(path))
            {
                return Persist(new(AppSettings.Defaults, SettingsStatus.Missing), backupReason: null);
            }

            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(AppSettings.Defaults, SettingsStatus.ReadFailed, Error: e);
        }

        var parsed = Parse(json);
        return parsed.Status switch
        {
            SettingsStatus.Migrated => Persist(parsed, $"v{parsed.FileVersion}"),
            SettingsStatus.Repaired => Persist(parsed, "invalid"),
            SettingsStatus.Corrupt => Persist(parsed, "corrupt"),

            // Loaded needs no write. FutureVersion belongs to a newer build: never touch it.
            _ => parsed,
        };
    }

    /// <summary>
    /// Turns a load result into fixed log events with numbers only. Never logs settings
    /// values, file contents, paths or exception messages.
    /// </summary>
    public static void LogOutcome(SettingsLoadResult result)
    {
        switch (result.Status)
        {
            case SettingsStatus.Loaded:
                Log.Info(LogEvent.SettingsLoaded);
                break;
            case SettingsStatus.Missing when result.Saved:
                Log.Info(LogEvent.SettingsCreated);
                break;
            case SettingsStatus.Migrated:
                Log.Info(LogEvent.SettingsMigrated, ("from_version", result.FileVersion));
                break;
            case SettingsStatus.Repaired:
                Log.Warn(LogEvent.SettingsRepaired, ("file_version", result.FileVersion));
                break;
            case SettingsStatus.Corrupt:
                Log.Warn(LogEvent.SettingsCorrupt);
                break;
            case SettingsStatus.FutureVersion:
                Log.Warn(
                    LogEvent.SettingsFutureVersion,
                    ("file_version", result.FileVersion),
                    ("supported_version", AppSettings.CurrentVersion));
                break;
            case SettingsStatus.ReadFailed:
                LogError(LogEvent.SettingsReadFailed, result.Error);
                break;
        }

        var neededWrite = result.Status is SettingsStatus.Missing or SettingsStatus.Migrated
            or SettingsStatus.Repaired or SettingsStatus.Corrupt;
        if (neededWrite && !result.Saved)
        {
            LogError(LogEvent.SettingsWriteFailed, result.Error);
        }
    }

    internal static SettingsLoadResult Parse(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, NodeOptions, DocumentOptions) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root is null || !TryReadVersion(root, out var version))
        {
            return new(AppSettings.Defaults, SettingsStatus.Corrupt);
        }

        // A newer build wrote this file. Its fields may mean something this build doesn't
        // know, so use defaults in memory and leave the file for that build.
        if (version > AppSettings.CurrentVersion)
        {
            return new(AppSettings.Defaults, SettingsStatus.FutureVersion, version);
        }

        for (var v = version; v < AppSettings.CurrentVersion; v++)
        {
            Migrations[v](root);
        }

        var (settings, repaired) = ReadFields(root);
        var status = repaired ? SettingsStatus.Repaired
            : version < AppSettings.CurrentVersion ? SettingsStatus.Migrated
            : SettingsStatus.Loaded;
        return new(settings, status, version);
    }

    private static void LogError(LogEvent logEvent, Exception? error)
    {
        if (error is null)
        {
            Log.Error(logEvent);
        }
        else
        {
            Log.Error(logEvent, error);
        }
    }

    /// <summary>
    /// Replaces settings.json with <paramref name="settings"/>, as the user chose them. Returns
    /// null once saved, or the error that prevented it; a failed save leaves the previous file
    /// intact. Unlike a repair, it makes no backup: the user asked for exactly this change.
    /// </summary>
    public Exception? Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Write(settings);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e;
        }
    }

    // The original is copied to a backup before anything is written, so a rewrite never loses
    // what the user had. If the backup or the write fails, the settings still apply in memory.
    private SettingsLoadResult Persist(SettingsLoadResult result, string? backupReason)
    {
        if (backupReason is not null)
        {
            try
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                File.Copy(path, $"{path}.{backupReason}-{stamp}.bak");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return result with { Error = e };
            }
        }

        return Save(result.Settings) is { } error ? result with { Error = error } : result with { Saved = true };
    }

    // Serialize first, write a temporary file, then swap it in, so a crash or a full disk
    // mid-write leaves the previous settings.json intact.
    private void Write(AppSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, WriteOptions);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .tmp is overwritten by the next successful write.
        }
    }

    // A missing version means a file from before versioning (version 0). A version that is
    // present but not a non-negative whole number makes the file corrupt.
    private static bool TryReadVersion(JsonObject root, out int version)
    {
        version = 0;
        if (!root.TryGetPropertyValue("version", out var node))
        {
            return true;
        }

        return node is JsonValue value && value.TryGetValue(out version) && version >= 0;
    }

    // Missing and unknown fields are fine. A field with the wrong type falls back to its
    // default and marks the settings as repaired.
    private static (AppSettings Settings, bool Repaired) ReadFields(JsonObject root)
    {
        var repaired = false;
        var fileLogging = ReadBool(root, "fileLogging", AppSettings.Defaults.FileLogging, ref repaired);
        var playSounds = ReadBool(root, "playSounds", AppSettings.Defaults.PlaySounds, ref repaired);
        // Missing or unreadable: the default; null: off on purpose.
        var hold = root.TryGetPropertyValue("holdShortcut", out var holdNode) && holdNode is null
            ? null
            : ReadChord(root, "holdShortcut", ref repaired) ?? AppSettings.Defaults.HoldShortcut;

        var provider = ReadName(
            root, "transcriptionProvider", AppSettings.Defaults.TranscriptionProvider, ref repaired);
        var localModel = ReadName(root, "localModel", AppSettings.Defaults.LocalModel, ref repaired);
        var defaults = AppSettings.Defaults;
        var settings = defaults with
        {
            FileLogging = fileLogging,
            PlaySounds = playSounds,
            HoldShortcut = hold,
            ToggleShortcut = ReadChord(root, "toggleShortcut", ref repaired),
            CustomHoldShortcut = ReadChord(root, "customHoldShortcut", ref repaired),
            CustomToggleShortcut = ReadChord(root, "customToggleShortcut", ref repaired),
            CustomPasteAgainShortcut = ReadChord(root, "customPasteAgainShortcut", ref repaired),
            StartDelayMilliseconds = ReadInt(
                root, "startDelayMilliseconds", 0, AppSettings.IsValidStartDelay, ref repaired),
            TranscriptionProvider = provider,
            LocalModel = localModel,
            TranscriptionService = ReadName(root, "transcriptionService", defaults.TranscriptionService, ref repaired),
            TranscriptionBaseUrl = ReadUrl(root, "transcriptionBaseUrl", defaults.TranscriptionBaseUrl, ref repaired),
            TranscriptionTimeoutSeconds = ReadTimeout(
                root, "transcriptionTimeoutSeconds", defaults.TranscriptionTimeoutSeconds, ref repaired),
            TranscriptionModel = ReadModel(root, "transcriptionModel", defaults.TranscriptionModel, ref repaired),
            Language = ReadOptional(root, "language", AppSettings.IsValidLanguage, ref repaired),
            Microphone = ReadOptional(root, "microphone", AppSettings.IsValidMicrophone, ref repaired),
            Words = ReadList(root, "vocabulary", AppSettings.IsValidWord, ref repaired),
            LanguageModelProvider = ReadName(
                root, "languageModelProvider", defaults.LanguageModelProvider, ref repaired),
            LocalLanguageModel = ReadName(root, "localLanguageModel", defaults.LocalLanguageModel, ref repaired),
            LanguageModelService = ReadName(root, "languageModelService", defaults.LanguageModelService, ref repaired),
            LanguageModelBaseUrl = ReadUrl(root, "languageModelBaseUrl", defaults.LanguageModelBaseUrl, ref repaired),
            LanguageModelTimeoutSeconds = ReadTimeout(
                root, "languageModelTimeoutSeconds", defaults.LanguageModelTimeoutSeconds, ref repaired),
            LanguageModelName = ReadModel(root, "languageModelName", defaults.LanguageModelName, ref repaired),
            LanguageModelFallbackName = ReadOptional(
                root, "languageModelFallbackName", AppSettings.IsValidModel, ref repaired),
            Cleanup = ReadName(root, "cleanup", defaults.Cleanup, ref repaired),
            CleanupPrompt = ReadOptional(root, "cleanupPrompt", AppSettings.IsValidCleanupPrompt, ref repaired),
            CleanupPromptVersion = ReadOptional(
                root, "cleanupPromptVersion", AppSettings.IsValidPromptVersion, ref repaired),
            InstructionGuard = ReadBool(root, "instructionGuard", defaults.InstructionGuard, ref repaired),
            OutputLanguage = ReadOptional(root, "outputLanguage", AppSettings.IsValidOutputLanguage, ref repaired),
            ErrorSound = ReadBool(root, "errorSound", defaults.ErrorSound, ref repaired),
            SoundVolume = ReadInt(root, "soundVolume", defaults.SoundVolume, AppSettings.IsValidVolume, ref repaired),
            Theme = ReadName(root, "theme", defaults.Theme, ref repaired),
            ShowTrayIcon = ReadBool(root, "showTrayIcon", defaults.ShowTrayIcon, ref repaired),
            HistoryRetention = ReadName(root, "historyRetention", defaults.HistoryRetention, ref repaired),
            PressEnterCommand = ReadBool(root, "pressEnterCommand", defaults.PressEnterCommand, ref repaired),
            PasteAgainShortcut = ReadChord(root, "pasteAgainShortcut", ref repaired),
            OverlayTimer = ReadBool(root, "overlayTimer", defaults.OverlayTimer, ref repaired),
            OverlayStyle = ReadName(root, "overlayStyle", defaults.OverlayStyle, ref repaired),
            OverlayMonitor = ReadOptional(root, "overlayMonitor", AppSettings.IsValidMonitor, ref repaired),
            MuteWhileDictating = ReadBool(root, "muteWhileDictating", defaults.MuteWhileDictating, ref repaired),
            Context = ReadBool(root, "context", defaults.Context, ref repaired),
            ContextExcludedApps = ReadList(root, "contextExcludedApps", AppSettings.IsValidWord, ref repaired),
            ContextScreenshot = ReadBool(root, "contextScreenshot", defaults.ContextScreenshot, ref repaired),
            ScreenshotSize = ReadInt(
                root, "screenshotSize", defaults.ScreenshotSize, AppSettings.IsValidScreenshotSize, ref repaired),
            ScreenshotWholeDesktop = ReadBool(
                root, "screenshotWholeDesktop", defaults.ScreenshotWholeDesktop, ref repaired),
            ContextWindowTitle = ReadBool(root, "contextWindowTitle", defaults.ContextWindowTitle, ref repaired),
            ContextModelName = ReadOptional(root, "contextModelName", AppSettings.IsValidModel, ref repaired),
            ContextPrompt = ReadOptional(root, "contextPrompt", AppSettings.IsValidCleanupPrompt, ref repaired),
            ContextPromptVersion = ReadOptional(
                root, "contextPromptVersion", AppSettings.IsValidPromptVersion, ref repaired),
            EditMode = ReadName(root, "editMode", defaults.EditMode, ref repaired),
            EditModeKey = ReadName(root, "editModeKey", defaults.EditModeKey, ref repaired),
            LivePreview = ReadBool(root, "livePreview", defaults.LivePreview, ref repaired),
            LiveTyping = ReadBool(root, "liveTyping", defaults.LiveTyping, ref repaired),
            LiveService = ReadName(root, "liveService", defaults.LiveService, ref repaired),
            LiveModel = ReadOptional(root, "liveModel", AppSettings.IsValidModel, ref repaired),
            KeepMeetingAudio = ReadBool(root, "keepMeetingAudio", defaults.KeepMeetingAudio, ref repaired),
            MeetingDetection = ReadName(root, "meetingDetection", defaults.MeetingDetection, ref repaired),
            MeetingIgnoredApps = ReadList(root, "meetingIgnoredApps", AppSettings.IsValidWord, ref repaired),
            RecordingFlagFile = ReadBool(root, "recordingFlagFile", defaults.RecordingFlagFile, ref repaired),
        };
        // Shortcuts that can't work together, as written by hand: off is safer than a clash.
        if (Shortcuts.Problem(settings) is not null)
        {
            repaired = true;
            settings = Shortcuts.Repair(settings);
        }

        return (settings, repaired);
    }
}
