using Naad.Cleanup;

namespace Naad.Tests.Cleanup;

public sealed class RateLimitCooldownsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"naad-cool-{Guid.NewGuid():N}.json");
    private readonly Clock _clock = new();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Start_ShortWait_CoolsInMemoryOnlyUntilItEnds()
    {
        var cooldowns = new RateLimitCooldowns(_path, _clock);

        cooldowns.Start("m", TimeSpan.FromMinutes(1), daily: false);

        Assert.NotNull(cooldowns.For("m"));
        Assert.Null(cooldowns.For("other"));
        Assert.False(File.Exists(_path));
        _clock.Now += TimeSpan.FromMinutes(1);
        Assert.Null(cooldowns.For("m"));
    }

    [Fact]
    public void Start_Daily_IsKeptAcrossRestarts()
    {
        new RateLimitCooldowns(_path, _clock).Start("m", TimeSpan.FromMinutes(5), daily: true);

        var restarted = new RateLimitCooldowns(_path, _clock);

        Assert.Equal(new Cooldown(_clock.Now + TimeSpan.FromMinutes(5), true), restarted.For("m"));
    }

    [Fact]
    public void For_DamagedFile_CountsAsEmpty()
    {
        File.WriteAllText(_path, "[");

        Assert.Null(new RateLimitCooldowns(_path, _clock).For("m"));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
