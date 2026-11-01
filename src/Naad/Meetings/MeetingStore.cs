using System.Security.Cryptography;
using System.Text.Json;

namespace Naad.Meetings;

/// <summary>
/// Meetings on disk, in %LOCALAPPDATA%\Naad\meetings: one encrypted file per meeting, and, only
/// when the user keeps meeting audio, one per recorded piece. Every file is DPAPI ciphertext for
/// the current Windows user. Nothing here logs what a file holds. Thread-safe.
/// </summary>
/// <param name="folder">Where the files are kept.</param>
internal sealed class MeetingStore(string folder)
{
    private const string MeetingExtension = ".meeting";
    private const string AudioExtension = ".audio";

    // Ties the ciphertext to Naad's meetings, as the history's does. Not a secret.
    private static readonly byte[] Entropy = "Naad meetings v1"u8.ToArray();

    private readonly Lock _gate = new();

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "meetings");

    /// <summary>Stores or replaces a meeting. Returns false if the disk refused.</summary>
    public bool Save(MeetingRecord meeting) =>
        TryWrite(meeting.Id + MeetingExtension, JsonSerializer.SerializeToUtf8Bytes(meeting));

    /// <summary>Stores one recorded piece of a meeting, for a user who keeps meeting audio.</summary>
    public bool SaveAudio(Guid id, int piece, byte[] wav) =>
        TryWrite($"{id}.{piece:D5}{AudioExtension}", wav);

    /// <summary>Every meeting, newest first. Files that don't decrypt or parse are skipped.</summary>
    public IReadOnlyList<MeetingRecord> All()
    {
        lock (_gate)
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            var meetings = new List<MeetingRecord>();
            foreach (var file in Directory.EnumerateFiles(folder, "*" + MeetingExtension))
            {
                try
                {
                    if (JsonSerializer.Deserialize<MeetingRecord>(Unprotect(File.ReadAllBytes(file))) is { } meeting)
                    {
                        meetings.Add(meeting);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException
                    or JsonException)
                {
                    Log.Warn(LogEvent.MeetingUnreadable);
                }
            }

            return [.. meetings.OrderByDescending(m => m.Started)];
        }
    }

    /// <summary>Deletes a meeting and any audio kept with it, at once.</summary>
    public void Delete(Guid id) => DeleteMatching(id + ".*");

    /// <summary>Deletes every meeting and recording, at once, including files that no longer decrypt.</summary>
    public void DeleteAll() => DeleteMatching("*");

    private void DeleteMatching(string pattern)
    {
        lock (_gate)
        {
            if (Directory.Exists(folder))
            {
                // A snapshot of the names, so nothing is deleted while it is being listed.
                foreach (var file in Directory.GetFiles(folder, pattern))
                {
                    TryDelete(file);
                }
            }
        }
    }

    private bool TryWrite(string name, byte[] content)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, name);

                // Write then swap, so a crash mid-write never leaves half a meeting.
                var temp = file + ".tmp";
                File.WriteAllBytes(temp, ProtectedData.Protect(content, Entropy, DataProtectionScope.CurrentUser));
                File.Move(temp, file, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Log.Error(LogEvent.MeetingWriteFailed, e);
                return false;
            }
        }
    }

    private static byte[] Unprotect(byte[] content) =>
        ProtectedData.Unprotect(content, Entropy, DataProtectionScope.CurrentUser);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.MeetingDeleteFailed, e);
        }
    }
}
