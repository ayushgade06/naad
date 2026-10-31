using System.Security.Cryptography;
using System.Text;

namespace Naad.Settings;

internal enum ApiKeySource
{
    /// <summary>No usable key: none is stored and the override variable isn't set.</summary>
    None,

    /// <summary>The development override, a service's NAAD_&lt;SERVICE&gt;_API_KEY variable.</summary>
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

    /// <summary>The override variable holds characters an API key can't contain.</summary>
    Invalid,
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
    // Ties the ciphertext to Naad, so a DPAPI blob made by another program for this user
    // doesn't decrypt as a Naad key. Not a secret; changing it orphans stored keys.
    private static readonly byte[] Entropy = "Naad API key v1"u8.ToArray();

    // Naad 1.0's single key; ServiceKeys moves it to its service's file. Local, not roaming,
    // AppData: a key never follows the profile to another machine, where DPAPI couldn't decrypt it.
    public static string DefaultPath { get; } = Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
        "Naad",
        "api-key.dpapi");

    /// <summary>
    /// Whether <paramref name="key"/> can be an API key: visible ASCII only, since it is sent in
    /// an HTTP header. Text pasted or piped from elsewhere can carry a byte-order mark or a
    /// non-breaking space, which would make every request fail before it is sent.
    /// </summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key) && key.All(c => c is > ' ' and < '\u007f');

    /// <summary>
    /// Reads a key piped to standard input as UTF-8, dropping a byte-order mark and the
    /// surrounding whitespace. PowerShell starts piped text with a UTF-8 byte-order mark, which
    /// the console's code page would otherwise turn into three stray characters.
    /// </summary>
    public static string ReadKey(Stream input)
    {
        using var reader = new StreamReader(input, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd().Trim().TrimStart('\uFEFF');
    }

    /// <summary>
    /// The key to use. A non-blank <paramref name="overrideValue"/> (normally the
    /// service's NAAD_&lt;SERVICE&gt;_API_KEY environment variable) wins and the store isn't read.
    /// </summary>
    public ApiKeyResult Resolve(string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
        {
            var trimmed = overrideValue.Trim();
            return IsValidKey(trimmed)
                ? new(trimmed, ApiKeySource.Environment, SecretStatus.Loaded)
                : new(null, ApiKeySource.None, SecretStatus.Invalid);
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
            return IsValidKey(key)
                ? new(key, ApiKeySource.Stored, SecretStatus.Loaded)
                : new(null, ApiKeySource.None, SecretStatus.Unreadable);
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
    /// <exception cref="ArgumentException">The key is blank or not <see cref="IsValidKey"/>
    /// after trimming; callers check first.</exception>
    public Exception? Save(string key)
    {
        var trimmed = key.Trim();
        if (!IsValidKey(trimmed))
        {
            throw new ArgumentException("Not a valid API key.", nameof(key));
        }

        var temp = path + ".tmp";
        try
        {
            var protectedKey = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(trimmed), Entropy, DataProtectionScope.CurrentUser);
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
            case (SecretStatus.Invalid, _):
                Log.Error(LogEvent.ApiKeyInvalid);
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
