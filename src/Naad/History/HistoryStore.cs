using System.Security.Cryptography;
using System.Text.Json;

namespace Naad.History;

/// <summary>How long dictations are kept in the history.</summary>
internal enum HistoryRetention
{
    /// <summary>None are kept, and those kept before are deleted.</summary>
    Off,

    /// <summary>Seven days.</summary>
    Week,

    /// <summary>Thirty days.</summary>
    Month,

    /// <summary>Until the user deletes them.</summary>
    Forever,
}

/// <summary>
/// The history on disk: one encrypted file per dictation, and one per kept recording, in
/// %LOCALAPPDATA%\Naad\history. Each file is DPAPI ciphertext for the current Windows user, so it
/// is unreadable from another account or PC. Nothing here logs what a file holds. Thread-safe.
/// </summary>
/// <param name="folder">Where the files are kept.</param>
/// <param name="time">Tests replace it.</param>
// ponytail: reads and decrypts every record to list them; a few thousand take well under a
// second. Add an encrypted index if histories kept Forever grow far past that.
internal sealed class HistoryStore(string folder, TimeProvider time)
{
    private const string RecordExtension = ".record";
    private const string AudioExtension = ".audio";

    // Ties the ciphertext to Naad's history: a DPAPI blob made by another program, or for Naad's
    // keys, doesn't decrypt as a record. Not a secret; changing it orphans the history.
    private static readonly byte[] Entropy = "Naad history v1"u8.ToArray();

    private readonly Lock _gate = new();

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "history");

    /// <summary>How old a record may get under <paramref name="retention"/>; null for no limit.</summary>
    public static TimeSpan? MaxAge(HistoryRetention retention) => retention switch
    {
        HistoryRetention.Off => TimeSpan.Zero,
        HistoryRetention.Week => TimeSpan.FromDays(7),
        HistoryRetention.Month => TimeSpan.FromDays(30),
        _ => null,
    };

    /// <summary>Stores a dictation and, if given, its recording. Returns false if the disk refused.</summary>
    public bool Add(HistoryRecord record, byte[]? wav)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(folder);
                if (wav is not null)
                {
                    Write(Path.Combine(folder, record.Id + AudioExtension), wav);
                }

                Write(
                    Path.Combine(folder, record.Id + RecordExtension),
                    JsonSerializer.SerializeToUtf8Bytes(record with { HasAudio = wav is not null }));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Log.Error(LogEvent.HistoryWriteFailed, e);
                return false;
            }
        }
    }

    /// <summary>Every record, newest first. Files that don't decrypt or parse are skipped.</summary>
    public IReadOnlyList<HistoryRecord> All()
    {
        lock (_gate)
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            var records = new List<HistoryRecord>();
            foreach (var file in Directory.EnumerateFiles(folder, "*" + RecordExtension))
            {
                try
                {
                    if (JsonSerializer.Deserialize<HistoryRecord>(Read(file)) is { } record)
                    {
                        records.Add(record);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException
                    or JsonException)
                {
                    Log.Warn(LogEvent.HistoryRecordUnreadable);
                }
            }

            return [.. records.OrderByDescending(r => r.Time)];
        }
    }

    /// <summary>The recording kept with <paramref name="id"/>, or null.</summary>
    public byte[]? Audio(Guid id)
    {
        lock (_gate)
        {
            try
            {
                var file = Path.Combine(folder, id + AudioExtension);
                return File.Exists(file) ? Read(file) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Log.Warn(LogEvent.HistoryRecordUnreadable);
                return null;
            }
        }
    }

    /// <summary>Deletes one dictation and its recording, at once.</summary>
    public void Delete(Guid id)
    {
        lock (_gate)
        {
            TryDelete(Path.Combine(folder, id + RecordExtension));
            TryDelete(Path.Combine(folder, id + AudioExtension));
        }
    }

    /// <summary>Deletes every dictation and recording, at once.</summary>
    public void DeleteAll()
    {
        lock (_gate)
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    TryDelete(file);
                }
            }
        }
    }

    /// <summary>
    /// Deletes what <paramref name="retention"/> no longer keeps: everything when Off. Returns how
    /// many dictations were deleted.
    /// </summary>
    public int Enforce(HistoryRetention retention)
    {
        if (MaxAge(retention) is not { } age)
        {
            return 0;
        }

        if (retention == HistoryRetention.Off)
        {
            var count = All().Count;
            DeleteAll();
            return count;
        }

        var cutoff = time.GetUtcNow() - age;
        var expired = All().Where(r => retention == HistoryRetention.Off || r.Time < cutoff).ToList();
        foreach (var record in expired)
        {
            Delete(record.Id);
        }

        return expired.Count;
    }

    private static void Write(string file, byte[] content)
    {
        // Write then swap, so a crash mid-write never leaves half a record.
        var temp = file + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(content, Entropy, DataProtectionScope.CurrentUser));
        File.Move(temp, file, overwrite: true);
    }

    private static byte[] Read(string file) =>
        ProtectedData.Unprotect(File.ReadAllBytes(file), Entropy, DataProtectionScope.CurrentUser);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.HistoryDeleteFailed, e);
        }
    }
}
