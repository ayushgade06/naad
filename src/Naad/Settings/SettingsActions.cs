using System.Diagnostics;
using Naad.Audio;
using Naad.Providers;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>
/// What the settings window's buttons do once it hands over: store keys, write settings.json and
/// the start-at-login entry, download models and test keys, logging each outcome without user
/// content. Applying the new settings to the running app stays with the caller. Use on the UI thread.
/// </summary>
/// <param name="store">settings.json.</param>
/// <param name="keys">The API keys, one per service.</param>
/// <param name="startup">The start-at-login entry.</param>
/// <param name="canSave">False when a newer Naad wrote settings.json, which this build never replaces.</param>
/// <param name="http">Shared and owned by the caller.</param>
internal sealed class SettingsActions(
    SettingsStore store,
    ServiceKeys keys,
    StartupEntry startup,
    bool canSave,
    HttpClient http)
{
    /// <summary>
    /// Stores the new keys, if any. Returns what went wrong, or null; on failure the settings
    /// should not be changed. A key stored before the failure stays stored.
    /// </summary>
    public string? SaveKeys(SettingsChange change)
    {
        foreach (var (service, key) in change.NewKeys)
        {
            if (keys.Save(service, key) is { } error)
            {
                Log.Error(LogEvent.ApiKeyStoreFailed, error, ("service", (int)service));
                return "An API key couldn't be saved, so the settings weren't changed. Try again.";
            }

            Log.Info(LogEvent.ApiKeyStored, ("service", (int)service));
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
        // Identifies the file in the log without naming it: each model has its own size.
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

    /// <summary>
    /// Transcribes a second of silence with <paramref name="transcriber"/>, the one in use, to
    /// check it works: an empty transcript is a pass.
    /// </summary>
    public static async Task<TranscriptionTest> TestTranscriptionAsync(
        ITranscriber? transcriber, CancellationToken cancellationToken)
    {
        if (transcriber is null)
        {
            return new(0, null, NotSetUp: true);
        }

        var silence = Wav.Encode(new byte[AudioRecorder.Format.AverageBytesPerSecond], AudioRecorder.Format);
        var started = Stopwatch.GetTimestamp();
        var result = await transcriber.TranscribeAsync(silence, cancellationToken);
        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Log.Info(
            LogEvent.TranscriptionTested,
            ("error", result.Failure is { } f ? (int)f.Error : -1),
            ("request_ms", milliseconds));
        return new(milliseconds, result.Failure);
    }

    /// <summary>Asks a service whether a key works, logging the outcome but never the key.</summary>
    public async Task<KeyTestResult> TestKeyAsync(
        CloudService service, string? key, Uri customUrl, CancellationToken cancellationToken)
    {
        var result = await ServiceKeyCheck.TestAsync(http, service, key, customUrl, cancellationToken);
        switch (result.Outcome)
        {
            case KeyTestOutcome.Accepted:
                Log.Info(LogEvent.ApiKeyTestAccepted, ("service", (int)service));
                break;
            case KeyTestOutcome.CannotVerify:
                Log.Info(LogEvent.ApiKeyTestUnverifiable, ("service", (int)service));
                break;
            default:
                Log.Warn(LogEvent.ApiKeyTestFailed, ("service", (int)service), ("code", result.Failure?.Code ?? 0));
                break;
        }

        return result;
    }
}
