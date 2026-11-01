using Naad.Context;
using Naad.Hotkey;
using Naad.Settings;
using Naad.Transcription;

namespace Naad.Tests.Settings;

// Every test uses its own temporary directory; the real %APPDATA%\Naad is never touched.
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "naad-settings-tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private SettingsLoadResult LoadFrom(string json)
    {
        File.WriteAllText(SettingsPath, json);
        return new SettingsStore(SettingsPath).Load();
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.Missing, result.Status);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }

    [Fact]
    public void Load_NoFile_TranscribesOnThisPc()
    {
        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(TranscriptionProvider.Local, result.Settings.TranscriptionProvider);
    }

    [Fact]
    public void Load_ValidCurrentFile_ReturnsItsValues()
    {
        var result = LoadFrom("""{ "version": 5, "fileLogging": false }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Load_MissingField_UsesItsDefault()
    {
        var result = LoadFrom("""{ "version": 5 }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(AppSettings.Defaults.FileLogging, result.Settings.FileLogging);
    }

    [Fact]
    public void Load_UnknownFields_AreIgnored()
    {
        var result = LoadFrom("""{ "version": 5, "fileLogging": false, "someFutureOption": { "x": [1, 2] } }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Load_HandEditedFile_AcceptsCommentsTrailingCommasAndCasing()
    {
        var result = LoadFrom("""
            {
              // turned off on purpose
              "Version": 5,
              "FileLogging": false,
            }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Load_UnversionedFile_MigratesFromVersionZeroKeepingValues()
    {
        var result = LoadFrom("""{ "fileLogging": false }""");

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.Equal(0, result.FileVersion);
        Assert.Equal(AppSettings.CurrentVersion, result.Settings.Version);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Migrations_CoverEveryVersionBelowCurrent()
    {
        Assert.Equal(AppSettings.CurrentVersion, SettingsStore.MigrationCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "version": "1" }""")]
    [InlineData("""{ "version": 1.5 }""")]
    [InlineData("""{ "version": -1 }""")]
    public void Load_EmptyMalformedOrBadVersion_ReturnsDefaultsAsCorrupt(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Corrupt, result.Status);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }

    [Theory]
    [InlineData("""{ "version": 5, "fileLogging": "no" }""")]
    [InlineData("""{ "version": 5, "fileLogging": 0 }""")]
    [InlineData("""{ "version": 5, "fileLogging": null }""")]
    public void Load_WrongTypedValue_FallsBackToDefaultAsRepaired(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults.FileLogging, result.Settings.FileLogging);
    }

    [Theory]
    [InlineData("RightCtrl")]
    [InlineData("leftctrl + space")]
    [InlineData("LeftAlt+LeftShift+F9")]
    public void Load_ValidShortcut_ReturnsIt(string text)
    {
        var result = LoadFrom($$"""{ "version": 5, "holdShortcut": "{{text}}" }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.True(Chord.TryParse(text, out var expected));
        Assert.Equal(expected, result.Settings.HoldShortcut);
    }

    [Fact]
    public void Load_NoShortcut_UsesRightCtrl()
    {
        var result = LoadFrom("""{ "version": 5, "fileLogging": true }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(Chord.RightCtrl, result.Settings.HoldShortcut);
    }

    [Theory]
    [InlineData("\"NoSuchKey\"")]
    [InlineData("\"\"")]
    [InlineData("\"ControlKey\"")]
    [InlineData("\"RightCtrl+RightCtrl\"")]
    [InlineData("163")]
    [InlineData("[\"RightCtrl\"]")]
    [InlineData("null")]
    public void Load_InvalidShortcut_FallsBackToRightCtrlAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "fileLogging": false, "holdShortcut": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(Chord.RightCtrl, result.Settings.HoldShortcut);
        Assert.False(result.Settings.FileLogging);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Load_PlaySounds_ReturnsIt(string value, bool expected)
    {
        var result = LoadFrom($$"""{ "version": 5, "playSounds": {{value}} }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings.PlaySounds);
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void Load_InvalidPlaySounds_FallsBackToOffAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "playSounds": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.False(result.Settings.PlaySounds);
    }

    // Tones cost a second or more per dictation on PCs whose speakers sleep, so they are opt-in;
    // a file that already chose them keeps them (Load_PlaySounds_ReturnsIt).
    [Fact]
    public void Defaults_PlaySounds_IsOff()
    {
        Assert.False(AppSettings.Defaults.PlaySounds);
    }

    [Fact]
    public void Load_NoTranscriptionSettings_UsesGroqDefaults()
    {
        var result = LoadFrom("""{ "version": 5 }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal("https://api.groq.com/openai/v1", result.Settings.TranscriptionBaseUrl.AbsoluteUri);
        Assert.Equal("whisper-large-v3-turbo", result.Settings.TranscriptionModel);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "whisper-1")]
    [InlineData("https://stt.example.test/openai/v1/", "openai/whisper-large-v3")]
    [InlineData("http://localhost:8000/v1", "Systran/faster-whisper-small")]
    [InlineData("http://127.0.0.1:9000/v1", "local-model")]
    public void Load_ValidTranscriptionSettings_ReturnsThem(string baseUrl, string model)
    {
        var result = LoadFrom(
            $$"""{ "version": 5, "transcriptionBaseUrl": "{{baseUrl}}", "transcriptionModel": "{{model}}" }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(new Uri(baseUrl), result.Settings.TranscriptionBaseUrl);
        Assert.Equal(model, result.Settings.TranscriptionModel);
    }

    // Plain http to another machine would send the API key unencrypted.
    [Theory]
    [InlineData("\"http://api.example.test/v1\"")]
    [InlineData("\"ftp://api.example.test/v1\"")]
    [InlineData("\"api.groq.com/openai/v1\"")]
    [InlineData("\"https://api.example.test/v1?key=1\"")]
    [InlineData("\"https://user:secret@api.example.test/v1\"")]
    [InlineData("\"\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void Load_InvalidBaseUrl_FallsBackToGroqAsRepaired(string value)
    {
        var result = LoadFrom(
            $$"""{ "version": 5, "transcriptionModel": "whisper-1", "transcriptionBaseUrl": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults.TranscriptionBaseUrl, result.Settings.TranscriptionBaseUrl);
        Assert.Equal("whisper-1", result.Settings.TranscriptionModel);
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"whisper large\"")]
    [InlineData("\"whisper\\t1\"")]
    [InlineData("\"whïsper\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void Load_InvalidModel_FallsBackToDefaultAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "transcriptionModel": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults.TranscriptionModel, result.Settings.TranscriptionModel);
    }

    [Fact]
    public void Load_ModelOver100Characters_FallsBackAsRepaired()
    {
        var result = LoadFrom($$"""{ "version": 5, "transcriptionModel": "{{new string('m', 101)}}" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
    }

    [Theory]
    [InlineData("""{ "version": 5 }""")]
    [InlineData("""{ "version": 5, "language": null, "microphone": null }""")]
    [InlineData("""{ "version": 5, "language": "", "microphone": "" }""")]
    public void Load_LanguageAndMicrophoneUnset_LeavesThemNull(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Null(result.Settings.Language);
        Assert.Null(result.Settings.Microphone);
    }

    [Theory]
    [InlineData("en", "Microphone (Test Device)")]
    [InlineData("haw", "M")]
    [InlineData("hi", "Thirty-one characters, exactly.")]
    public void Load_ValidLanguageAndMicrophone_ReturnsThem(string language, string microphone)
    {
        var result = LoadFrom(
            $$"""{ "version": 5, "language": "{{language}}", "microphone": "{{microphone}}" }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(language, result.Settings.Language);
        Assert.Equal(microphone, result.Settings.Microphone);
    }

    [Theory]
    [InlineData("\"EN\"")]
    [InlineData("\"e\"")]
    [InlineData("\"english\"")]
    [InlineData("\"en-US\"")]
    [InlineData("5")]
    public void Load_InvalidLanguage_FallsBackToUnsetAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "language": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Language);
    }

    [Theory]
    [InlineData("\"Thirty-two characters, exactly!!\"")]
    [InlineData("\"Mic\\u0007\"")]
    [InlineData("true")]
    public void Load_InvalidMicrophone_FallsBackToDefaultDeviceAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "microphone": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Microphone);
    }

    [Theory]
    [InlineData("""{ "version": 5 }""", null)]
    [InlineData("""{ "version": 5, "vocabulary": ["Naad", "Groq"] }""", "Naad, Groq")]
    [InlineData("""{ "version": 5, "vocabulary": [] }""", null)]
    public void Load_Vocabulary_ReturnsIt(string json, string? vocabulary)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(vocabulary, result.Settings.Vocabulary);
    }

    [Theory]
    [InlineData("RightCtrl", false, "RightCtrl", null)]
    [InlineData("LeftCtrl+Space", true, null, "LeftCtrl+Space")]
    public void Load_Version4Shortcut_KeepsItsRole(string old, bool handsFree, string? hold, string? toggle)
    {
        var result = LoadFrom($$"""
            { "version": 4, "dictationShortcut": "{{old}}", "handsFree": {{(handsFree ? "true" : "false")}},
              "pasteAgainShortcut": "RightAlt+V" }
            """);

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.Equal(hold, result.Settings.HoldShortcut?.ToString());
        Assert.Equal(toggle, result.Settings.ToggleShortcut?.ToString());
        Assert.Equal("RightAlt+V", result.Settings.PasteAgainShortcut?.ToString());
        Assert.DoesNotContain("dictationShortcut", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Version4WithoutShortcut_HoldsRightCtrl() =>
        Assert.Equal(Chord.RightCtrl, LoadFrom("""{ "version": 4 }""").Settings.HoldShortcut);

    [Fact]
    public void Load_Version4HandsFreeOnly_TogglesRightCtrl()
    {
        var result = LoadFrom("""{ "version": 4, "handsFree": true, "fileLogging": false }""");

        Assert.Null(result.Settings.HoldShortcut);
        Assert.Equal(Chord.RightCtrl, result.Settings.ToggleShortcut);
        Assert.False(result.Settings.FileLogging);
    }

    [Theory]
    [InlineData("\"Bogus\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void Load_Version4HandsFreeWithAnUnreadableShortcut_TogglesRightCtrl(string shortcut)
    {
        var result = LoadFrom($$"""{ "version": 4, "handsFree": true, "dictationShortcut": {{shortcut}} }""");

        Assert.Null(result.Settings.HoldShortcut);
        Assert.Equal(Chord.RightCtrl, result.Settings.ToggleShortcut);
    }

    [Fact]
    public void Load_MigratedHandsFreeFile_StaysHandsFreeWhenReadAgain()
    {
        LoadFrom("""{ "version": 4, "handsFree": true, "dictationShortcut": "F9" }""");

        var reloaded = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.Loaded, reloaded.Status);
        Assert.Null(reloaded.Settings.HoldShortcut);
        Assert.Equal("F9", reloaded.Settings.ToggleShortcut?.ToString());
    }

    [Fact]
    public void Load_Version4ManualWithShiftInTheShortcut_KeepsPasteAgainAndTheShortcut()
    {
        var result = LoadFrom("""
            { "version": 4, "dictationShortcut": "RightCtrl+RightShift", "editMode": "Manual",
              "pasteAgainShortcut": "RightAlt+V" }
            """);

        Assert.Equal("RightCtrl+RightShift", result.Settings.HoldShortcut?.ToString());
        Assert.Equal("RightAlt+V", result.Settings.PasteAgainShortcut?.ToString());
        Assert.Equal(EditMode.Automatic, result.Settings.EditMode);
    }

    [Fact]
    public void Load_Version4ShortcutSameAsPasteAgain_DropsOnlyPasteAgain()
    {
        var result = LoadFrom("""{ "version": 4, "dictationShortcut": "F9", "pasteAgainShortcut": "F9" }""");

        Assert.Equal("F9", result.Settings.HoldShortcut?.ToString());
        Assert.Null(result.Settings.PasteAgainShortcut);
    }

    [Fact]
    public void Load_InvalidHoldWithAToggle_KeepsHoldOnRightCtrl()
    {
        var result = LoadFrom("""{ "version": 5, "holdShortcut": "NoSuchKey", "toggleShortcut": "F9" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(Chord.RightCtrl, result.Settings.HoldShortcut);
        Assert.Equal("F9", result.Settings.ToggleShortcut?.ToString());
    }

    [Fact]
    public void Load_OverlayAndMute_ReturnsThem()
    {
        var result = LoadFrom("""
            { "version": 5, "overlayStyle": "Compact", "overlayMonitor": "Primary", "muteWhileDictating": true }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(Naad.Tray.OverlayStyle.Compact, result.Settings.OverlayStyle);
        Assert.Equal("Primary", result.Settings.OverlayMonitor);
        Assert.True(result.Settings.MuteWhileDictating);
    }

    [Theory]
    [InlineData("""{ "version": 5, "overlayStyle": "Huge" }""")]
    [InlineData("""{ "version": 5, "overlayMonitor": 5 }""")]
    [InlineData("""{ "version": 5, "muteWhileDictating": "yes" }""")]
    public void Load_InvalidOverlayOrMute_FallsBackAsRepaired(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults.OverlayStyle, result.Settings.OverlayStyle);
        Assert.Null(result.Settings.OverlayMonitor);
        Assert.False(result.Settings.MuteWhileDictating);
    }

    [Fact]
    public void Load_ContextModelPromptAndTitle_ReturnsThem()
    {
        var result = LoadFrom("""
            { "version": 5, "contextWindowTitle": true, "contextModelName": "synthetic-vision",
              "contextPrompt": "Say what is going on.", "contextPromptVersion": "2026-11-01" }
            """);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.True(result.Settings.ContextWindowTitle);
        Assert.Equal("synthetic-vision", result.Settings.ContextModelName);
        Assert.Equal("Say what is going on.", result.Settings.ContextPrompt);
        Assert.Equal("2026-11-01", result.Settings.ContextPromptVersion);
    }

    [Fact]
    public void Load_Defaults_KeepTheWindowTitleOff() => Assert.False(AppSettings.Defaults.ContextWindowTitle);

    [Fact]
    public void Load_ToggleOnly_KeepsHoldOff()
    {
        var result = LoadFrom("""{ "version": 5, "holdShortcut": null, "toggleShortcut": "F9" }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Null(result.Settings.HoldShortcut);
    }

    [Theory]
    [InlineData("""{ "version": 5, "holdShortcut": "F9", "toggleShortcut": "F9" }""")]
    [InlineData("""{ "version": 5, "holdShortcut": "F9", "pasteAgainShortcut": "F9" }""")]
    [InlineData("""{ "version": 5, "holdShortcut": "A" }""")]
    [InlineData("""{ "version": 5, "holdShortcut": "RightShift", "editMode": "Manual" }""")]
    public void Load_ClashingShortcuts_RepairedSoTheyWorkTogether(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(Shortcuts.Problem(result.Settings));
    }

    [Theory]
    [InlineData("""{ "version": 5, "vocabulary": "not a list" }""")]
    [InlineData("""{ "version": 5, "vocabulary": 5 }""")]
    [InlineData("""{ "version": 5, "startDelayMilliseconds": 900 }""")]
    [InlineData("""{ "version": 5, "editModeKey": "Alt" }""")]
    public void Load_InvalidVocabularyOrShortcutOption_FallsBackAsRepaired(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Vocabulary);
        Assert.Equal(0, result.Settings.StartDelayMilliseconds);
    }

    [Fact]
    public void Load_VocabularyOver500Characters_FallsBackAsRepaired()
    {
        var result = LoadFrom($$"""{ "version": 5, "vocabulary": "{{new string('v', 501)}}" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
    }

    [Theory]
    [InlineData("""{ "version": 5 }""", "Local")]
    [InlineData("""{ "version": 5, "transcriptionProvider": "Local" }""", "Local")]
    [InlineData("""{ "version": 5, "transcriptionProvider": "local" }""", "Local")]
    [InlineData("""{ "version": 5, "transcriptionProvider": "CLOUD" }""", "Cloud")]
    public void Load_TranscriptionProvider_ReturnsIt(string json, string expected)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings.TranscriptionProvider.ToString());
    }

    [Theory]
    [InlineData("\"offline\"")]
    [InlineData("\"1\"")]
    [InlineData("\"Cloud, Local\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Load_InvalidTranscriptionProvider_FallsBackToLocalAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "transcriptionProvider": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(TranscriptionProvider.Local, result.Settings.TranscriptionProvider);
    }

    [Theory]
    [InlineData("""{ "version": 5 }""", "Base")]
    [InlineData("""{ "version": 5, "localModel": "Turbo" }""", "Turbo")]
    [InlineData("""{ "version": 5, "localModel": "turbo" }""", "Turbo")]
    [InlineData("""{ "version": 5, "localModel": "BASE" }""", "Base")]
    public void Load_LocalModel_ReturnsIt(string json, string expected)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings.LocalModel.ToString());
    }

    [Theory]
    [InlineData("\"large\"")]
    [InlineData("\"1\"")]
    [InlineData("\"Base, Turbo\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void Load_InvalidLocalModel_FallsBackToBaseAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 5, "localModel": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(WhisperModel.Base, result.Settings.LocalModel);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", "Cloud")]
    [InlineData("""{ "fileLogging": true }""", "Cloud")]
    [InlineData("""{ "version": 1, "transcriptionProvider": "Local" }""", "Local")]
    [InlineData("""{ "version": 1, "TranscriptionProvider": "Local" }""", "Local")]
    [InlineData("""{ "version": 1, "transcriptionProvider": "Cloud" }""", "Cloud")]
    public void Load_FileFromBeforeLocalDefault_KeepsItsProvider(string json, string expected)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.Equal(expected, result.Settings.TranscriptionProvider.ToString());
    }

    [Fact]
    public void Load_RewrittenFile_KeepsShortcutReadable()
    {
        LoadFrom("""{ "fileLogging": false, "dictationShortcut": "leftctrl+space" }""");

        var reloaded = new SettingsStore(SettingsPath).Load();

        var written = File.ReadAllText(SettingsPath);
        Assert.Contains("\"holdShortcut\": \"LeftCtrl+Space\"", written, StringComparison.Ordinal);
        Assert.Equal(SettingsStatus.Loaded, reloaded.Status);
        Assert.Equal("LeftCtrl+Space", reloaded.Settings.HoldShortcut?.ToString());
    }

    [Fact]
    public void Load_FutureVersion_UsesDefaultsAndLeavesFileUntouched()
    {
        const string NewerFile = """{ "version": 99, "fileLogging": false, "newThing": 1 }""";

        var result = LoadFrom(NewerFile);

        Assert.Equal(SettingsStatus.FutureVersion, result.Status);
        Assert.Equal(99, result.FileVersion);
        Assert.Equal(AppSettings.Defaults, result.Settings);
        Assert.Equal(NewerFile, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_FileLockedByAnotherProcess_ReturnsDefaultsAsReadFailed()
    {
        File.WriteAllText(SettingsPath, """{ "version": 5, "fileLogging": false }""");
        using var exclusive = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.ReadFailed, result.Status);
        Assert.IsType<IOException>(result.Error);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }
}
