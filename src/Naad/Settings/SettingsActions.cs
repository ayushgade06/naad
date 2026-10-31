using System.Diagnostics;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>
/// What the settings window's buttons do once it hands over: store keys, write settings.json and
/// the start-at-login entry, download models and test keys, logging each outcome without user
/// content. Applying the new settings to the running app stays with the caller. Use on the UI thread.
/// </summary>
/// <param name="store">settings.json.</param>
/// <param name="keys">The transcription key.</param>
/// <param name="languageModelKeys">The language model key.</param>
/// <param name="startup">The start-at-login entry.</param>
/// <param name="canSave">False when a newer Naad wrote settings.json, which this build never replaces.</param>
/// <param name="http">Shared and owned by the caller.</param>
internal sealed class SettingsActions(
    SettingsStore store,
    SecretStore keys,
    SecretStore languageModelKeys,
    StartupEntry startup,
    bool canSave,
    HttpClient http)
{
    /// <summary>
    /// Stores the new keys, if any. Returns what went wrong, or null; on failure nothing else
    /// should be changed, so a key that can't be stored never leaves the settings half-saved.
    /// </summary>
    public string? SaveKeys(SettingsChange change)
    {
        foreach (var (key, secrets) in new[] { (change.NewKey, keys), (change.NewLanguageModelKey, languageModelKeys) })
        {
            if (key is null)
            {
                continue;
            }

            if (secrets.Save(key) is { } error)
            {
                Log.Error(LogEvent.ApiKeyStoreFailed, error);
                return "The API key couldn't be saved, so nothing was changed. Try again.";
            }

            Log.Info(LogEvent.ApiKeyStored);
        }

        return null;
    }

    /// <summary>
    /// Writes the start-at-login entry and settings.json. Returns what couldn't be saved, or null.
    /// Settings that can't be written still apply until Naad exits, and the message says so.
    /// </summary>
    public string? SaveSettings(SettingsChange change)
    {
        string? problem = null;

        // Written whenever ticked, so an entry left by a moved copy points here again.
        var startedAtLogin = startup.IsEnabled();
        if (change.StartAtLogin || startedAtLogin)
        {
            if (startup.Set(change.StartAtLogin) is { } startupError)
            {
                Log.Error(LogEvent.StartAtLoginFailed, startupError);
                problem = "Naad couldn't change whether it starts when you sign in. ";
            }
            else if (change.StartAtLogin != startedAtLogin)
            {
                Log.Info(change.StartAtLogin ? LogEvent.StartAtLoginEnabled : LogEvent.StartAtLoginDisabled);
            }
        }

        if (!canSave)
        {
            Log.Warn(LogEvent.SettingsSaveSkippedNewerFile);
            problem += "A newer version of Naad wrote settings.json, so these changes apply only until Naad exits.";
        }
        else if (store.Save(change.Settings) is { } writeError)
        {
            Log.Error(LogEvent.SettingsWriteFailed, writeError);
            problem += "settings.json couldn't be written, so these changes apply only until Naad exits.";
        }
        else
        {
            Log.Info(LogEvent.SettingsSaved);
        }

        return problem?.TrimEnd();
    }

    /// <summary>Downloads a model file, logging its size in megabytes and the outcome.</summary>
    public async Task<ModelDownloadResult> DownloadModelAsync(
        LocalModel model, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await model.DownloadAsync(http, progress, cancellationToken);
        var megabytes = model.Size / 1_000_000;
        if (result.Outcome == ModelDownloadOutcome.Downloaded)
        {
            Log.Info(
                LogEvent.LocalModelDownloaded,
                ("mb", megabytes),
                ("download_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        }
        else
        {
            Log.Warn(
                LogEvent.LocalModelDownloadFailed,
                ("mb", megabytes),
                ("outcome", (int)result.Outcome),
                ("code", result.Code));
        }

        return result;
    }

    /// <summary>Asks a provider whether a key works, logging the outcome but never the key.</summary>
    public async Task<KeyTestResult> TestKeyAsync(string key, Uri baseUrl, CancellationToken cancellationToken)
    {
        var result = await OpenAiCompatibleTranscriber.TestKeyAsync(http, key, baseUrl, cancellationToken);
        switch (result.Outcome)
        {
            case KeyTestOutcome.Accepted:
                Log.Info(LogEvent.ApiKeyTestAccepted);
                break;
            case KeyTestOutcome.CannotVerify:
                Log.Info(LogEvent.ApiKeyTestUnverifiable);
                break;
            default:
                Log.Warn(LogEvent.ApiKeyTestFailed, ("code", result.Failure?.Code ?? 0));
                break;
        }

        return result;
    }
}
