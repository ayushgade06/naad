using System.Text.Json;

namespace Naad.Updates;

/// <summary>
/// What Naad remembers about updates between runs, in %LOCALAPPDATA%\Naad\update-state.json: a
/// version the user chose to skip, the one the overlay last mentioned, and when GitHub last
/// answered a check. No user content.
/// </summary>
/// <param name="Skipped">A version the user skipped; null for none.</param>
/// <param name="Marked">The version the overlay already said is available, so it says so once.</param>
/// <param name="LastChecked">When a check last got an answer from GitHub; null before the first.</param>
internal sealed record UpdateState(string? Skipped = null, string? Marked = null, DateTimeOffset? LastChecked = null)
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
    /// Writes it to <paramref name="path"/>; a failure only means a release is offered again.
    /// </summary>
    public void Save(string path)
    {
        try
        {
            // Written whole, then swapped in: a crash mid-write never loses a skipped version.
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(this));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.UpdateStateUnsaved, e);
        }
    }
}
