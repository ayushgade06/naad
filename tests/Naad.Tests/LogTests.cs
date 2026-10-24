namespace Naad.Tests;

// Log is static; every test here points it at its own temporary directory.
public sealed class LogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "naad-log-tests", Guid.NewGuid().ToString("N"));

    private string LogText => File.ReadAllText(Path.Combine(_directory, Log.FileName));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Format_EventWithFields_WritesTimestampLevelEventAndFields()
    {
        var time = new DateTimeOffset(2026, 10, 25, 10, 15, 30, 123, TimeSpan.Zero);

        var line = Log.Format(time, "INFO", LogEvent.Pasted, null, [("release_to_text_ms", 640), ("chars", 21)]);

        Assert.Equal("2026-10-25T10:15:30.123Z INFO Pasted release_to_text_ms=640 chars=21", line);
    }

    [Theory]
    [InlineData("hello world this is a test")]
    [InlineData("Hello")]
    [InlineData("sk-secret")]
    [InlineData("")]
    [InlineData("a_name_that_is_much_longer_than_thirty_two_chars")]
    public void Info_UnsafeFieldName_IsReplacedAndNeverWritten(string mistake)
    {
        Log.Configure(_directory);

        Log.Info(LogEvent.TranscriptionCompleted, (mistake, 1));

        Assert.Contains("TranscriptionCompleted invalid_field=1", LogText, StringComparison.Ordinal);
        if (mistake.Length > 0)
        {
            Assert.DoesNotContain(mistake, LogText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Error_WithException_WritesTypeButNeverMessage()
    {
        Log.Configure(_directory);
        var error = new InvalidOperationException("transcript or fake-api-key-0123 in a message");

        Log.Error(LogEvent.DictationFailed, error);

        Assert.Contains("ERROR DictationFailed exception=System.InvalidOperationException", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("fake-api-key", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Info_FileReachesLimit_RotatesToSingleBackup()
    {
        Log.Configure(_directory, maxBytes: 200);

        for (var i = 0; i < 20; i++)
        {
            Log.Info(LogEvent.RecordingStopped, ("held_ms", i), ("wav_bytes", 1000));
        }

        var current = new FileInfo(Path.Combine(_directory, Log.FileName));
        var backup = new FileInfo(Path.Combine(_directory, Log.BackupFileName));
        Assert.True(backup.Exists);
        Assert.True(current.Length < 200 + 100, $"current log is {current.Length} bytes");
        Assert.Equal([Log.BackupFileName, Log.FileName], Directory.GetFiles(_directory).Select(Path.GetFileName).Order());
        Assert.Contains("held_ms=19", LogText, StringComparison.Ordinal);
    }
}
