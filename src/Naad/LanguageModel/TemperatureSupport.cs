using System.Text.Json;

namespace Naad.LanguageModel;

/// <summary>
/// Remembers which cloud models refused a <c>temperature</c>, as some reasoning models do, so
/// later requests leave it out instead of failing first. Kept in a small file for a week, then
/// tried again, since providers change what their models accept. Holds endpoints and model ids
/// only, never anything the user said.
/// </summary>
/// <param name="path">The file; a missing or damaged one counts as empty.</param>
/// <param name="time">Tests replace it.</param>
internal sealed class TemperatureSupport(string path, TimeProvider time)
{
    /// <summary>How long a refusal is remembered.</summary>
    public static readonly TimeSpan Remembered = TimeSpan.FromDays(7);

    private readonly Lock _gate = new();
    private Dictionary<string, DateTimeOffset>? _refused;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "temperature-refused.json");

    /// <summary>Whether <paramref name="model"/> at <paramref name="baseUrl"/> refused a temperature this week.</summary>
    public bool IsRefused(Uri baseUrl, string model)
    {
        lock (_gate)
        {
            return Load().TryGetValue(Key(baseUrl, model), out var until) && until > time.GetUtcNow();
        }
    }

    /// <summary>Records a refusal. A file that can't be written only costs one more refused request.</summary>
    public void MarkRefused(Uri baseUrl, string model)
    {
        lock (_gate)
        {
            var refused = Load();
            var now = time.GetUtcNow();
            foreach (var expired in refused.Where(p => p.Value <= now).Select(p => p.Key).ToList())
            {
                refused.Remove(expired);
            }

            refused[Key(baseUrl, model)] = now + Remembered;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(refused));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(LogEvent.TemperatureSupportWriteFailed, e);
            }
        }
    }

    private static string Key(Uri baseUrl, string model) => baseUrl.AbsoluteUri.TrimEnd('/') + " " + model;

    private Dictionary<string, DateTimeOffset> Load()
    {
        if (_refused is not null)
        {
            return _refused;
        }

        try
        {
            _refused = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(path))
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _refused = null;
        }

        return _refused ??= [];
    }
}
