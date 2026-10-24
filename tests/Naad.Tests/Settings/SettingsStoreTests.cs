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
