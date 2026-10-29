using Naad.Hotkey;
using Naad.Settings;

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
    public void Load_ValidCurrentFile_ReturnsItsValues()
    {
        var result = LoadFrom("""{ "version": 1, "fileLogging": false }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Load_MissingField_UsesItsDefault()
    {
        var result = LoadFrom("""{ "version": 1 }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(AppSettings.Defaults.FileLogging, result.Settings.FileLogging);
    }

    [Fact]
    public void Load_UnknownFields_AreIgnored()
    {
        var result = LoadFrom("""{ "version": 1, "fileLogging": false, "someFutureOption": { "x": [1, 2] } }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.False(result.Settings.FileLogging);
    }

    [Fact]
    public void Load_HandEditedFile_AcceptsCommentsTrailingCommasAndCasing()
    {
        var result = LoadFrom("""
            {
              // turned off on purpose
              "Version": 1,
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
    [InlineData("""{ "version": 1, "fileLogging": "no" }""")]
    [InlineData("""{ "version": 1, "fileLogging": 0 }""")]
    [InlineData("""{ "version": 1, "fileLogging": null }""")]
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
        var result = LoadFrom($$"""{ "version": 1, "dictationShortcut": "{{text}}" }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.True(Chord.TryParse(text, out var expected));
        Assert.Equal(expected, result.Settings.DictationShortcut);
    }

    [Fact]
    public void Load_NoShortcut_UsesRightCtrl()
    {
        var result = LoadFrom("""{ "version": 1, "fileLogging": true }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(Chord.RightCtrl, result.Settings.DictationShortcut);
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
        var result = LoadFrom($$"""{ "version": 1, "fileLogging": false, "dictationShortcut": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(Chord.RightCtrl, result.Settings.DictationShortcut);
        Assert.False(result.Settings.FileLogging);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Load_PlaySounds_ReturnsIt(string value, bool expected)
    {
        var result = LoadFrom($$"""{ "version": 1, "playSounds": {{value}} }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(expected, result.Settings.PlaySounds);
    }

    [Theory]
    [InlineData("\"no\"")]
    [InlineData("0")]
    [InlineData("null")]
    public void Load_InvalidPlaySounds_FallsBackToOnAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 1, "playSounds": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.True(result.Settings.PlaySounds);
    }

    [Fact]
    public void Load_NoTranscriptionSettings_UsesGroqDefaults()
    {
        var result = LoadFrom("""{ "version": 1 }""");

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
            $$"""{ "version": 1, "transcriptionBaseUrl": "{{baseUrl}}", "transcriptionModel": "{{model}}" }""");

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
    [InlineData("\"\"")]
    [InlineData("5")]
    [InlineData("null")]
    public void Load_InvalidBaseUrl_FallsBackToGroqAsRepaired(string value)
    {
        var result = LoadFrom(
            $$"""{ "version": 1, "transcriptionModel": "whisper-1", "transcriptionBaseUrl": {{value}} }""");

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
        var result = LoadFrom($$"""{ "version": 1, "transcriptionModel": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Equal(AppSettings.Defaults.TranscriptionModel, result.Settings.TranscriptionModel);
    }

    [Fact]
    public void Load_ModelOver100Characters_FallsBackAsRepaired()
    {
        var result = LoadFrom($$"""{ "version": 1, "transcriptionModel": "{{new string('m', 101)}}" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""")]
    [InlineData("""{ "version": 1, "language": null, "microphone": null }""")]
    [InlineData("""{ "version": 1, "language": "", "microphone": "" }""")]
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
            $$"""{ "version": 1, "language": "{{language}}", "microphone": "{{microphone}}" }""");

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
        var result = LoadFrom($$"""{ "version": 1, "language": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Language);
    }

    [Theory]
    [InlineData("\"Thirty-two characters, exactly!!\"")]
    [InlineData("\"Mic\\u0007\"")]
    [InlineData("true")]
    public void Load_InvalidMicrophone_FallsBackToDefaultDeviceAsRepaired(string value)
    {
        var result = LoadFrom($$"""{ "version": 1, "microphone": {{value}} }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Microphone);
    }

    [Theory]
    [InlineData("""{ "version": 1 }""", null, false)]
    [InlineData("""{ "version": 1, "vocabulary": "Naad, Groq", "handsFree": true }""", "Naad, Groq", true)]
    [InlineData("""{ "version": 1, "vocabulary": "", "handsFree": false }""", null, false)]
    public void Load_VocabularyAndHandsFree_ReturnsThem(string json, string? vocabulary, bool handsFree)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.Equal(vocabulary, result.Settings.Vocabulary);
        Assert.Equal(handsFree, result.Settings.HandsFree);
    }

    [Theory]
    [InlineData("""{ "version": 1, "vocabulary": "line one\nline two" }""")]
    [InlineData("""{ "version": 1, "vocabulary": 5 }""")]
    [InlineData("""{ "version": 1, "handsFree": "yes" }""")]
    [InlineData("""{ "version": 1, "handsFree": null }""")]
    public void Load_InvalidVocabularyOrHandsFree_FallsBackAsRepaired(string json)
    {
        var result = LoadFrom(json);

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.Null(result.Settings.Vocabulary);
        Assert.False(result.Settings.HandsFree);
    }

    [Fact]
    public void Load_VocabularyOver500Characters_FallsBackAsRepaired()
    {
        var result = LoadFrom($$"""{ "version": 1, "vocabulary": "{{new string('v', 501)}}" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
    }

    [Fact]
    public void Load_RewrittenFile_KeepsShortcutReadable()
    {
        LoadFrom("""{ "fileLogging": false, "dictationShortcut": "leftctrl+space" }""");

        var reloaded = new SettingsStore(SettingsPath).Load();

        var written = File.ReadAllText(SettingsPath);
        Assert.Contains("\"dictationShortcut\": \"LeftCtrl+Space\"", written, StringComparison.Ordinal);
        Assert.Equal(SettingsStatus.Loaded, reloaded.Status);
        Assert.Equal("LeftCtrl+Space", reloaded.Settings.DictationShortcut.ToString());
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
        File.WriteAllText(SettingsPath, """{ "version": 1, "fileLogging": false }""");
        using var exclusive = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.ReadFailed, result.Status);
        Assert.IsType<IOException>(result.Error);
        Assert.Equal(AppSettings.Defaults, result.Settings);
    }
}
