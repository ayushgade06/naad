using Naad.History;

namespace Naad.Tests.History;

// Each test uses its own temporary folder; the real history is never touched.
[Collection("Log")]
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"naad-history-{Guid.NewGuid():N}");
    private readonly Clock _clock = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Add_RecordAndAudio_ReadBackNewestFirst()
    {
        var store = new HistoryStore(_folder, _clock);
        var older = Record(_clock.Now.AddMinutes(-5), "first synthetic dictation");
        var newer = Record(_clock.Now, "second synthetic dictation");

        Assert.True(store.Add(older, wav: null));
        Assert.True(store.Add(newer, wav: [1, 2, 3]));

        Assert.Equal([newer with { HasAudio = true }, older], store.All());
        Assert.Equal([1, 2, 3], store.Audio(newer.Id));
        Assert.Null(store.Audio(older.Id));
    }

    [Fact]
    public void Add_FilesOnDisk_HoldNoPlainText()
    {
        var store = new HistoryStore(_folder, _clock);

        store.Add(Record(_clock.Now, "a distinctive synthetic phrase"), wav: "RIFFsynthetic"u8.ToArray());

        foreach (var file in Directory.EnumerateFiles(_folder))
        {
            var bytes = File.ReadAllText(file, System.Text.Encoding.Latin1);
            Assert.DoesNotContain("distinctive", bytes, StringComparison.Ordinal);
            Assert.DoesNotContain("RIFF", bytes, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Delete_OneAndAll_RemovesFilesAtOnce()
    {
        var store = new HistoryStore(_folder, _clock);
        var kept = Record(_clock.Now, "kept");
        var gone = Record(_clock.Now, "gone");
        store.Add(kept, wav: [1]);
        store.Add(gone, wav: [2]);

        store.Delete(gone.Id);
        Assert.Equal([kept.Id], store.All().Select(r => r.Id));

        store.DeleteAll();
        Assert.Empty(store.All());
        Assert.Empty(Directory.EnumerateFiles(_folder));
    }

    [Theory]
    [InlineData("Week", 2)]
    [InlineData("Month", 1)]
    [InlineData("Forever", 0)]
    [InlineData("Off", 3)]
    public void Enforce_Retention_DeletesOnlyWhatIsTooOld(string retention, int deleted)
    {
        var store = new HistoryStore(_folder, _clock);
        store.Add(Record(_clock.Now.AddDays(-1), "recent"), null);
        store.Add(Record(_clock.Now.AddDays(-10), "last week"), null);
        store.Add(Record(_clock.Now.AddDays(-40), "last month"), null);

        Assert.Equal(deleted, store.Enforce(Enum.Parse<HistoryRetention>(retention)));
        Assert.Equal(3 - deleted, store.All().Count);
    }

    [Fact]
    public void All_DamagedFile_IsSkipped()
    {
        var store = new HistoryStore(_folder, _clock);
        store.Add(Record(_clock.Now, "fine"), null);
        File.WriteAllBytes(Path.Combine(_folder, $"{Guid.NewGuid()}.record"), [9, 9, 9]);

        Assert.Single(store.All());
    }

    [Fact]
    public void ToString_Record_NeverShowsText()
    {
        Assert.DoesNotContain("secret", Record(_clock.Now, "secret words").ToString(), StringComparison.Ordinal);
    }

    private static HistoryRecord Record(DateTimeOffset time, string text) =>
        new(Guid.NewGuid(), time, "notepad", 2_000, text, text, 300, 0, HistoryOutcome.Pasted, false);

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
