using System.Text.Json;

namespace Naad.Cleanup;

/// <summary>
/// Which cloud models hit a rate limit, and until when, so cleanup skips them instead of waiting
/// on another refusal. Short waits live in memory; a daily quota, or any wait of an hour or more,
/// is also kept in a small file so restarting Naad doesn't hammer the provider. Holds model ids
/// and times only.
/// </summary>
/// <param name="path">The file; a missing or damaged one counts as empty.</param>
/// <param name="time">Tests replace it.</param>
internal sealed class RateLimitCooldowns(string path, TimeProvider time)
{
    // Shorter waits are per-minute limits; not worth a file write.
    private static readonly TimeSpan KeptOnDisk = TimeSpan.FromHours(1);

    private readonly Lock _gate = new();
    private Dictionary<string, Cooldown>? _cooldowns;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "rate-limits.json");

    /// <summary>Until when <paramref name="model"/> is cooling down, or null if it can be used.</summary>
    public Cooldown? For(string model)
    {
        lock (_gate)
        {
            return Load().TryGetValue(model, out var cooldown) && cooldown.Until > time.GetUtcNow() ? cooldown : null;
        }
    }

    /// <summary>Starts a cooldown of <paramref name="wait"/> for <paramref name="model"/>.</summary>
    public void Start(string model, TimeSpan wait, bool daily)
    {
        lock (_gate)
        {
            var cooldowns = Load();
            var now = time.GetUtcNow();
            cooldowns[model] = new(now + wait, daily);
            if (!daily && wait < KeptOnDisk)
            {
                return;
            }

            try
            {
                var lasting = cooldowns
                    .Where(c => c.Value.Until > now && (c.Value.Daily || c.Value.Until - now >= KeptOnDisk))
                    .ToDictionary();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(lasting));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(LogEvent.RateLimitWriteFailed, e);
            }
        }
    }

    private Dictionary<string, Cooldown> Load()
    {
        if (_cooldowns is not null)
        {
            return _cooldowns;
        }

        try
        {
            _cooldowns = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Cooldown>>(File.ReadAllText(path))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _cooldowns = null;
        }

        return _cooldowns ??= [];
    }
}

/// <param name="Until">When the model can be used again.</param>
/// <param name="Daily">Whether the daily request quota ran out.</param>
internal sealed record Cooldown(DateTimeOffset Until, bool Daily);
