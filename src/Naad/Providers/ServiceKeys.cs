using Naad.Settings;

namespace Naad.Providers;

/// <summary>
/// One encrypted API key per <see cref="CloudService"/>, shared by transcription and AI, each in
/// its own DPAPI file (see <see cref="SecretStore"/>). A key stored by Naad 1.0, which had one key
/// for transcription, moves to the service its base URL belonged to the first time it is needed.
/// </summary>
/// <param name="folder">Where the key files are kept.</param>
/// <param name="legacyKey">Naad 1.0's single key file.</param>
/// <param name="legacyService">The service Naad 1.0's key was for.</param>
/// <param name="environment">Reads an override variable; tests replace it.</param>
internal sealed class ServiceKeys(
    string folder, string legacyKey, CloudService legacyService, Func<string, string?> environment)
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "keys");

    /// <summary>
    /// This Windows user's keys. Naad 1.0's single key belonged to <paramref name="legacyService"/>,
    /// the transcription service its settings named.
    /// </summary>
    public static ServiceKeys ForThisPc(CloudService legacyService) =>
        new(DefaultFolder, SecretStore.DefaultPath, legacyService, Environment.GetEnvironmentVariable);

    /// <summary>The key for <paramref name="service"/>: its override variable, else the stored one.</summary>
    public ApiKeyResult Resolve(CloudService service)
    {
        MoveLegacyKey();
        return Store(service).Resolve(environment(CloudServices.OverrideVariable(service)));
    }

    /// <summary>Stores <paramref name="key"/> for <paramref name="service"/>; null once saved, or the error.</summary>
    public Exception? Save(CloudService service, string key)
    {
        MoveLegacyKey();
        return Store(service).Save(key);
    }

    private SecretStore Store(CloudService service) => new(PathFor(service));

    private string PathFor(CloudService service) =>
        Path.Combine(folder, $"{service.ToString().ToLowerInvariant()}.dpapi");

    // The file holds the same DPAPI ciphertext either way, so it moves without being decrypted.
    private void MoveLegacyKey()
    {
        var target = PathFor(legacyService);
        try
        {
            if (File.Exists(legacyKey) && !File.Exists(target))
            {
                Directory.CreateDirectory(folder);
                File.Move(legacyKey, target);
                Log.Info(LogEvent.ApiKeyMoved, ("service", (int)legacyService));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The old file stays; the next start tries again.
            Log.Error(LogEvent.ApiKeyStoreFailed, e);
        }
    }
}
