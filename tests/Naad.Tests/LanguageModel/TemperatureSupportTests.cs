using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

public sealed class TemperatureSupportTests : IDisposable
{
    private static readonly Uri BaseUrl = new("https://llm.example.test/v1/");
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"naad-temperature-{Guid.NewGuid():N}.json");
    private readonly Clock _clock = new();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void IsRefused_NothingRecorded_False()
    {
        Assert.False(new TemperatureSupport(_path, _clock).IsRefused(BaseUrl, "m"));
    }

    [Fact]
    public void MarkRefused_SameModel_RefusedForAWeekOnly()
    {
        var support = new TemperatureSupport(_path, _clock);

        support.MarkRefused(BaseUrl, "m");

        Assert.True(support.IsRefused(BaseUrl, "m"));
        Assert.False(support.IsRefused(BaseUrl, "other"));
        _clock.Now += TemperatureSupport.Remembered;
        Assert.False(support.IsRefused(BaseUrl, "m"));
    }

    [Fact]
    public void MarkRefused_NewInstance_ReadsItBackFromTheFile()
    {
        new TemperatureSupport(_path, _clock).MarkRefused(BaseUrl, "m");

        Assert.True(new TemperatureSupport(_path, _clock).IsRefused(new("https://llm.example.test/v1"), "m"));
    }

    [Fact]
    public void IsRefused_DamagedFile_CountsAsEmpty()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.False(new TemperatureSupport(_path, _clock).IsRefused(BaseUrl, "m"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
