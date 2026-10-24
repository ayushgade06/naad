using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Naad.Settings;

/// <summary>What <see cref="SettingsStore.Load"/> found in the settings file.</summary>
internal enum SettingsStatus
{
    Loaded,
    Missing,
    Migrated,
    Repaired,
    Corrupt,
    FutureVersion,
    ReadFailed,
}

/// <param name="Settings">The settings to use; always valid.</param>
/// <param name="Status">What was found, for the caller to log.</param>
/// <param name="FileVersion">The schema version found in the file, when it had a readable one.</param>
/// <param name="Saved">Whether settings.json was written (created, migrated, repaired or replaced).</param>
/// <param name="Error">The exception behind a failed read or write, for logging its type.</param>
internal sealed record SettingsLoadResult(
    AppSettings Settings,
    SettingsStatus Status,
    int FileVersion = 0,
    bool Saved = false,
    Exception? Error = null);

/// <summary>
/// Reads settings.json. Every failure ends in usable settings: a missing, empty, malformed,
/// unreadable or newer-version file yields defaults, and a wrong-typed value falls back to its
/// default. A missing file is created with defaults. A migrated, repaired or corrupt file is
/// first copied to settings.json.&lt;reason&gt;-&lt;UTC time&gt;.bak and then rewritten. A newer-version
/// file is never written. Nothing here logs; the caller logs the returned status.
/// </summary>
internal sealed class SettingsStore(string path)
{
    // Hand-edited files may differ in casing, keep comments, or leave a trailing comma.
    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // Migrations[n] upgrades a version-n file to version n + 1, in place.
    private static readonly Action<JsonObject>[] Migrations =
    [
        // 0 -> 1: files written before the version field existed. Their fields already
        // mean what version 1 means, so only the version changes.
        _ => { },
    ];

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Naad", "settings.json");

    internal static int MigrationCount => Migrations.Length;

    public SettingsLoadResult Load()
    {
        string json;
        try
        {
            if (!File.Exists(path))
            {
                return Persist(new(AppSettings.Defaults, SettingsStatus.Missing), backupReason: null);
            }

            json = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(AppSettings.Defaults, SettingsStatus.ReadFailed, Error: e);
        }

        var parsed = Parse(json);
        return parsed.Status switch
        {
            SettingsStatus.Migrated => Persist(parsed, $"v{parsed.FileVersion}"),
            SettingsStatus.Repaired => Persist(parsed, "invalid"),
            SettingsStatus.Corrupt => Persist(parsed, "corrupt"),

            // Loaded needs no write. FutureVersion belongs to a newer build: never touch it.
            _ => parsed,
        };
    }

    internal static SettingsLoadResult Parse(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, NodeOptions, DocumentOptions) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root is null || !TryReadVersion(root, out var version))
        {
            return new(AppSettings.Defaults, SettingsStatus.Corrupt);
        }

        // A newer build wrote this file. Its fields may mean something this build doesn't
        // know, so use defaults in memory and leave the file for that build.
        if (version > AppSettings.CurrentVersion)
        {
            return new(AppSettings.Defaults, SettingsStatus.FutureVersion, version);
        }

        for (var v = version; v < AppSettings.CurrentVersion; v++)
        {
            Migrations[v](root);
        }

        var (settings, repaired) = ReadFields(root);
        var status = repaired ? SettingsStatus.Repaired
            : version < AppSettings.CurrentVersion ? SettingsStatus.Migrated
            : SettingsStatus.Loaded;
        return new(settings, status, version);
    }

    // The original is copied to a backup before anything is written, so a rewrite never loses
    // what the user had. If the backup or the write fails, the settings still apply in memory.
    private SettingsLoadResult Persist(SettingsLoadResult result, string? backupReason)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (backupReason is not null)
            {
                var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                File.Copy(path, $"{path}.{backupReason}-{stamp}.bak");
            }

            Write(result.Settings);
            return result with { Saved = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return result with { Error = e };
        }
    }

    // Serialize first, write a temporary file, then swap it in, so a crash or a full disk
    // mid-write leaves the previous settings.json intact.
    private void Write(AppSettings settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, WriteOptions);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .tmp is overwritten by the next successful write.
        }
    }

    // A missing version means a file from before versioning (version 0). A version that is
    // present but not a non-negative whole number makes the file corrupt.
    private static bool TryReadVersion(JsonObject root, out int version)
    {
        version = 0;
        if (!root.TryGetPropertyValue("version", out var node))
        {
            return true;
        }

        return node is JsonValue value && value.TryGetValue(out version) && version >= 0;
    }

    // Missing and unknown fields are fine. A field with the wrong type falls back to its
    // default and marks the settings as repaired.
    private static (AppSettings Settings, bool Repaired) ReadFields(JsonObject root)
    {
        var repaired = false;
        var fileLogging = AppSettings.Defaults.FileLogging;
        if (root.TryGetPropertyValue("fileLogging", out var node))
        {
            if (node?.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                fileLogging = node.GetValue<bool>();
            }
            else
            {
                repaired = true;
            }
        }

        return (AppSettings.Defaults with { FileLogging = fileLogging }, repaired);
    }
}
