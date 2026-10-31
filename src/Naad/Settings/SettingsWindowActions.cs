using Naad.Cleanup;
using Naad.Providers;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>What the settings window asks the app to do; every member runs on the UI thread.</summary>
/// <param name="KeyFor">Where a service's key comes from; the key itself is used only for tests.</param>
/// <param name="DownloadModel">Downloads a model file to run on this PC, reporting progress.</param>
/// <param name="TestKey">Checks a key (null for none) for a service and custom endpoint.</param>
/// <param name="TestTranscription">Transcribes a second of silence with the settings on screen and a key
/// (a typed one, or null for the stored one).</param>
/// <param name="TestPrompt">Sends cleanup instructions and a sample to the saved language model.</param>
/// <param name="CooldownFor">A cloud model's running rate limit, if any.</param>
/// <param name="Save">Saves and applies the change. Returns what went wrong, or null.</param>
/// <param name="BeginCapture">Starts capturing a shortcut; false if a dictation is running.</param>
/// <param name="CancelCapture">Stops a capture early.</param>
internal sealed record SettingsWindowActions(
    Func<CloudService, ApiKeyResult> KeyFor,
    Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> DownloadModel,
    Func<CloudService, string?, Uri, CancellationToken, Task<KeyTestResult>> TestKey,
    Func<AppSettings, string?, CancellationToken, Task<TranscriptionTest>> TestTranscription,
    Func<string?, string, CancellationToken, Task<PromptTest>> TestPrompt,
    Func<string, Cooldown?> CooldownFor,
    Func<SettingsChange, string?> Save,
    Func<bool> BeginCapture,
    Action CancelCapture);

/// <param name="Milliseconds">How long the request took.</param>
/// <param name="Failure">Why it failed, or null when the service answered.</param>
/// <param name="NotSetUp">True when nothing could be sent: no key, or no model on this PC.</param>
internal readonly record struct TranscriptionTest(
    long Milliseconds, TranscriptionFailure? Failure, bool NotSetUp = false);
