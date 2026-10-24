using Naad.Settings;

namespace Naad.Tests.Settings;

// Write-side behaviour of SettingsStore.Load. Temporary directories only.
public sealed class SettingsStoreWriteTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "naad-settings-tests", Guid.NewGuid().ToString("N"));

    public SettingsStoreWriteTests() => Directory.CreateDirectory(_directory);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string[] Backups(string reason) =>
        Directory.GetFiles(_directory, $"settings.json.{reason}-*.bak");

    // A directory where the temporary file should go makes every write fail, deterministically
    // and without changing file permissions.
    private void BlockWrites() => Directory.CreateDirectory(SettingsPath + ".tmp");

    [Fact]
    public void Load_NoFile_CreatesFileWithDefaults()
    {
        var result = new SettingsStore(SettingsPath).Load();

        Assert.True(result.Saved);
        Assert.Equal("""
            {
              "version": 1,
              "fileLogging": true
            }
            """.ReplaceLineEndings(), File.ReadAllText(SettingsPath).ReplaceLineEndings());
    }

    [Fact]
    public void Load_NoFolder_CreatesFolderAndFile()
    {
        var nested = Path.Combine(_directory, "Naad", "settings.json");

        var result = new SettingsStore(nested).Load();

        Assert.True(result.Saved);
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Load_SavedSettings_RoundTripOnNextLoad()
    {
        File.WriteAllText(SettingsPath, """{ "fileLogging": false }""");
        new SettingsStore(SettingsPath).Load();

        var reloaded = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.Loaded, reloaded.Status);
        Assert.False(reloaded.Settings.FileLogging);
    }

    [Theory]
    [InlineData("""{ "fileLogging": false }""", "v0")]
    [InlineData("""{ "version": 1, "fileLogging": "no" }""", "invalid")]
    [InlineData("{ not json", "corrupt")]
    public void Load_FileNeedingRewrite_BacksUpOriginalThenRewrites(string original, string reason)
    {
        File.WriteAllText(SettingsPath, original);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.True(result.Saved);
        var backup = Assert.Single(Backups(reason));
        Assert.Equal(original, File.ReadAllText(backup));
        Assert.Equal(SettingsStatus.Loaded, new SettingsStore(SettingsPath).Load().Status);
    }

    [Fact]
    public void Load_ValidCurrentFile_IsNotRewritten()
    {
        const string Original = """{ "version": 1, "fileLogging": false, "unknownKept": true }""";
        File.WriteAllText(SettingsPath, Original);

        var result = new SettingsStore(SettingsPath).Load();

        Assert.False(result.Saved);
        Assert.Equal(Original, File.ReadAllText(SettingsPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [Fact]
    public void Load_FutureVersion_IsNeitherRewrittenNorBackedUp()
    {
        File.WriteAllText(SettingsPath, """{ "version": 99 }""");

        var result = new SettingsStore(SettingsPath).Load();

        Assert.False(result.Saved);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_WriteFails_KeepsPreviousFileAndUsesMigratedSettingsInMemory()
    {
        const string Original = """{ "fileLogging": false }""";
        File.WriteAllText(SettingsPath, Original);
        BlockWrites();

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.Migrated, result.Status);
        Assert.False(result.Saved);
        Assert.NotNull(result.Error);
        Assert.False(result.Settings.FileLogging);
        Assert.Equal(Original, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_NoFileAndWriteFails_StillReturnsDefaults()
    {
        BlockWrites();

        var result = new SettingsStore(SettingsPath).Load();

        Assert.Equal(SettingsStatus.Missing, result.Status);
        Assert.False(result.Saved);
        Assert.Equal(AppSettings.Defaults, result.Settings);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_RewrittenFile_HoldsOnlyKnownSettingsNeverOtherContent()
    {
        // Someone pasted a key into the file; a rewrite must not carry it forward.
        File.WriteAllText(SettingsPath, """{ "fileLogging": true, "apiKey": "fake-api-key-0123", "note": "dictated text" }""");

        new SettingsStore(SettingsPath).Load();

        var written = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("fake-api-key-0123", written, StringComparison.Ordinal);
        Assert.DoesNotContain("dictated text", written, StringComparison.Ordinal);
        Assert.Equal(["fileLogging", "version"], System.Text.Json.Nodes.JsonNode.Parse(written)!.AsObject()
            .Select(p => p.Key).Order());
    }
}
