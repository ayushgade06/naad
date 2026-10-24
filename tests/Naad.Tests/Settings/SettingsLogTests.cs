using Naad.Settings;

namespace Naad.Tests.Settings;

[Collection("Log")]
public sealed class SettingsLogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "naad-settings-log-tests", Guid.NewGuid().ToString("N"));

    private string LogText => File.ReadAllText(Path.Combine(_directory, Log.FileName));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
        else if (File.Exists(_directory))
        {
            File.Delete(_directory);
        }
    }

    [Fact]
    public void LogOutcome_EachStatus_WritesFixedEventWithNumbersOnly()
    {
        var defaults = AppSettings.Defaults;
        (SettingsLoadResult Result, string Expected)[] outcomes =
        [
            (new(defaults, SettingsStatus.Loaded), "INFO SettingsLoaded"),
            (new(defaults, SettingsStatus.Missing, Saved: true), "INFO SettingsCreated"),
            (new(defaults, SettingsStatus.Migrated, 0, Saved: true), "INFO SettingsMigrated from_version=0"),
            (new(defaults, SettingsStatus.Repaired, 1, Saved: true), "WARN SettingsRepaired file_version=1"),
            (new(defaults, SettingsStatus.Corrupt, Saved: true), "WARN SettingsCorrupt"),
            (new(defaults, SettingsStatus.FutureVersion, 7),
                "WARN SettingsFutureVersion file_version=7 supported_version=1"),
            (new(defaults, SettingsStatus.ReadFailed, Error: new IOException("C:/Users/someone/secret")),
                "ERROR SettingsReadFailed exception=System.IO.IOException"),
            (new(defaults, SettingsStatus.Corrupt, Error: new UnauthorizedAccessException("denied")),
                "ERROR SettingsWriteFailed exception=System.UnauthorizedAccessException"),
        ];
        Log.Configure(_directory);

        foreach (var (result, _) in outcomes)
        {
            SettingsStore.LogOutcome(result);
        }

        foreach (var (_, expected) in outcomes)
        {
            Assert.Contains(expected, LogText, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("secret", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("denied", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void LogOutcome_LogFileUnwritable_DoesNotThrow()
    {
        Log.Configure(_directory);
        Directory.Delete(_directory, recursive: true);
        File.WriteAllText(_directory, "a file where the log folder should be");

        var exception = Record.Exception(() =>
        {
            SettingsStore.LogOutcome(new(AppSettings.Defaults, SettingsStatus.Corrupt, Error: new IOException()));
            SettingsStore.LogOutcome(new(AppSettings.Defaults, SettingsStatus.ReadFailed, Error: new IOException()));
        });

        Assert.Null(exception);
    }
}
