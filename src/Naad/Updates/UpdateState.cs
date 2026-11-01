using System.Text.Json;

namespace Naad.Updates;

/// <summary>
/// What Naad remembers about updates between runs, in %LOCALAPPDATA%\Naad\update-state.json: the
/// newest version seen, when it was first seen, and a version the user chose to skip. No user
/// content.
/// </summary>
/// <param name="Seen">The newest version a check found, such as "2.0.1"; null for none.</param>
/// <param name="FirstSeen">When <paramref name="Seen"/> was first found.</param>
/// <param name="Skipped">A version the user skipped; null for none.</param>
internal sealed record UpdateState(string? Seen = null, DateTimeOffset FirstSeen = default, string? Skipped = null)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "update-state.json");

    /// <summary>What <paramref name="path"/> holds, or nothing remembered when it is missing or unreadable.</summary>
    public static UpdateState Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllBytes(path)) ?? new()
                : new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error(LogEvent.UpdateStateUnreadable, e);
            return new();
        }
    }

    /// <summary>
    /// Writes it to <paramref name="path"/>; a failure only means a release waits or is offered again.
    /// </summary>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.UpdateStateUnsaved, e);
        }
    }
}
