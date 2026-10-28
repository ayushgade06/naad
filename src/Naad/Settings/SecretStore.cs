using System.Security.Cryptography;
using System.Text;

namespace Naad.Settings;

internal enum ApiKeySource
{
    /// <summary>No usable key: none is stored and the override variable isn't set.</summary>
    None,

    /// <summary>The development override, <see cref="SecretStore.OverrideVariable"/>.</summary>
    Environment,

    /// <summary>The key stored with DPAPI.</summary>
    Stored,
}

internal enum SecretStatus
{
    Loaded,
    Missing,

    /// <summary>The file exists but doesn't decrypt: damaged, or encrypted by another Windows user.</summary>
    Unreadable,

    /// <summary>The file couldn't be read at all.</summary>
    ReadFailed,
}

/// <param name="Key">The key, or null when <paramref name="Source"/> is None.</param>
/// <param name="Source">Where the key came from.</param>
/// <param name="Status">What was found in the store; Loaded when the override was used.</param>
/// <param name="Error">The exception behind an unreadable store, for logging its type only.</param>
internal sealed record ApiKeyResult(string? Key, ApiKeySource Source, SecretStatus Status, Exception? Error = null)
{
    // A record's generated ToString would print the key; nothing should ever be able to.
    public override string ToString() => $"ApiKeyResult {{ Source = {Source}, Status = {Status} }}";
}

/// <summary>
/// Keeps the transcription API key encrypted at rest with DPAPI, CurrentUser scope: only the
/// same Windows user on the same machine can decrypt it. The file holds ciphertext only, and
/// nothing here logs or puts the key in an exception.
/// </summary>
internal sealed class SecretStore(string path)
{
    /// <summary>For development: when set, it is used instead of the stored key.</summary>
    public const string OverrideVariable = "NAAD_GROQ_API_KEY";

    // Ties the ciphertext to Naad, so a DPAPI blob made by another program for this user
    // doesn't decrypt as a Naad key. Not a secret; changing it orphans stored keys.
    private static readonly byte[] Entropy = "Naad API key v1"u8.ToArray();

    // Local, not roaming, AppData: the key never follows the profile to another machine,
    // where DPAPI couldn't decrypt it anyway.
    public static string DefaultPath { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Naad",
        "api-key.dpapi");

    /// <summary>
    /// The key to use. A non-blank <paramref name="overrideValue"/> (normally the
    /// <see cref="OverrideVariable"/> environment variable) wins and the store isn't read.
    /// </summary>
    public ApiKeyResult Resolve(string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            return new(overrideValue.Trim(), ApiKeySource.Environment, SecretStatus.Loaded);
        }

        byte[] protectedKey;
        try
        {
            if (!File.Exists(path))
            {
                return new(null, ApiKeySource.None, SecretStatus.Missing);
            }

            protectedKey = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, ApiKeySource.None, SecretStatus.ReadFailed, e);
        }

        try
        {
            var key = Encoding.UTF8.GetString(
                ProtectedData.Unprotect(protectedKey, Entropy, DataProtectionScope.CurrentUser));
            return string.IsNullOrWhiteSpace(key)
                ? new(null, ApiKeySource.None, SecretStatus.Unreadable)
                : new(key, ApiKeySource.Stored, SecretStatus.Loaded);
        }
        catch (CryptographicException e)
        {
            return new(null, ApiKeySource.None, SecretStatus.Unreadable, e);
        }
    }

    /// <summary>
    /// Encrypts and stores <paramref name="key"/>, replacing any stored key. Returns null once
    /// saved, or the error that prevented it; a failed save leaves the previous file intact.
    /// </summary>
    /// <exception cref="ArgumentException">The key is blank.</exception>
    public Exception? Save(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var temp = path + ".tmp";
        try
        {
            var protectedKey = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write then swap, so a crash mid-write never leaves half a key behind.
            File.WriteAllBytes(temp, protectedKey);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
        {
            TryDelete(temp);
            return e;
        }
    }

    /// <summary>Logs where the key came from, never the key itself.</summary>
    public static void LogOutcome(ApiKeyResult result)
    {
        switch (result.Status, result.Source)
        {
            case (_, ApiKeySource.Environment):
                Log.Info(LogEvent.ApiKeyFromEnvironment);
                break;
            case (SecretStatus.Loaded, _):
                Log.Info(LogEvent.ApiKeyLoaded);
                break;
            case (SecretStatus.Missing, _):
                Log.Error(LogEvent.ApiKeyMissing);
                break;
            case (_, _) when result.Error is { } error:
                Log.Error(LogEvent.ApiKeyUnreadable, error);
                break;
            default:
                Log.Error(LogEvent.ApiKeyUnreadable);
                break;
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
            // Best effort: a leftover .tmp holds only ciphertext and is replaced by the next save.
        }
    }
}
