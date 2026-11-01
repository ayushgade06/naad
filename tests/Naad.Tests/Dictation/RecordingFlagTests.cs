using Naad.Dictation;

namespace Naad.Tests.Dictation;

[Collection("Log")]
public sealed class RecordingFlagTests
{
    [Fact]
    public void Update_FileExistsOnlyWhileRecordingAndHoldsTheStartTime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"naad-flag-{Guid.NewGuid():N}", "is-recording");
        var started = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

        RecordingFlag.Update(true, path, started);
        Assert.Equal(started.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            File.ReadAllText(path));
        RecordingFlag.Update(false, path, started);

        Assert.False(File.Exists(path));
        Directory.Delete(Path.GetDirectoryName(path)!);
    }
}
