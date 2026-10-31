using Naad.Providers;

namespace Naad.Settings;

/// <summary>
/// <c>Naad.exe --set-api-key</c>: stores the key piped to standard input for the transcription
/// service settings.json names, then exits. Console only: no settings are loaded or written, so no
/// log file is written either. Typing the key as an argument would leave it in the shell's
/// history, so only a pipe is read.
/// </summary>
internal static class SetApiKeyCommand
{
    /// <summary>Stores the key piped to standard input; returns the exit code.</summary>
    /// <param name="argument">The command-line switch, for the usage message.</param>
    public static int Run(string argument)
    {
        // Typing the key as an argument would leave it in the shell's history; only a pipe is read.
        var key = Console.IsInputRedirected ? SecretStore.ReadKey(Console.OpenStandardInput()) : "";
        if (string.IsNullOrWhiteSpace(key))
        {
            StartupError.Show($"Pipe the API key into Naad.exe {argument}, as docs/settings.md describes.");
            return 1;
        }

        if (!SecretStore.IsValidKey(key))
        {
            Log.Error(LogEvent.ApiKeyInvalid);
            StartupError.Show("That isn't a valid API key: it contains spaces or characters outside plain ASCII. " +
                "Nothing was saved.");
            return 1;
        }

        // The key is for the transcription service settings.json names, read without writing it.
        var service = AppSettings.Defaults.TranscriptionService;
        try
        {
            if (File.Exists(SettingsStore.DefaultPath))
            {
                var text = File.ReadAllText(SettingsStore.DefaultPath);
                service = SettingsStore.Parse(text).Settings.TranscriptionService;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A locked or unreadable file: the key goes to the default service, as for a new install.
        }
        if (ServiceKeys.ForThisPc(service).Save(service, key) is { } error)
        {
            Log.Error(LogEvent.ApiKeyStoreFailed, error);
            StartupError.Show("The API key could not be saved. The previously stored key, if any, is unchanged.");
            return 1;
        }

        Log.Info(LogEvent.ApiKeyStored);
        return 0;
    }
}
