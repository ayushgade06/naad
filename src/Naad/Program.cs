using System.Diagnostics;
using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Settings;
using Naad.Transcription;
using Naad.Tray;
using NAudio;
using Velopack;

namespace Naad;

internal static class Program
{
    // Stores the API key piped to standard input, then exits; see docs/settings.md.
    private const string SetApiKeyArgument = "--set-api-key";

    // The key in use, kept only to redact it from the fatal-error dialog; it changes when the
    // settings window saves a new one.
    private static string? _apiKey;

    [STAThread]
    private static int Main(string[] args)
    {
        // First, before anything else: the installer starts Naad.exe with its own arguments to
        // run these hooks, and Run exits the process once they are done. A build that wasn't
        // installed (dotnet run, CI) continues at once. Uninstalling stops Naad starting at sign-in;
        // settings, the key, the model and logs are user data and stay (see README).
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartupEntry.ForThisApp().Set(false))
            .Run();

        ApplicationConfiguration.Initialize();
        HandleUnhandledExceptions();

        // Needs no single-instance check: it only writes the key file. A running instance reads
        // it again only when its settings window saves.
        if (args is [SetApiKeyArgument])
        {
            return StoreApiKey();
        }

        // First, before any hook or microphone work: a second instance must leave without
        // registering a second keyboard hook. Released when Main returns, on this thread.
        using var instance = SingleInstanceGuard.TryAcquire();
        if (instance is null)
        {
            // Console only: the running instance owns the settings and the log file.
            Log.Info(LogEvent.SecondInstanceRejected);
            return 0;
        }

        var store = new SettingsStore(SettingsStore.DefaultPath);
        var loaded = LoadSettings(store);
        var keys = new SecretStore(SecretStore.DefaultPath);

        try
        {
            // Without a usable key Naad still starts, and opens its settings window to ask for one.
            Run(store, loaded, keys, ResolveKey(keys, log: true));
            Log.Info(LogEvent.AppStopped);
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.FatalError, e);
            StartupError.Show($"Naad stopped because of an error.\n\n{StartupError.Describe(e, _apiKey)}");
            return 1;
        }
    }

    // Console only: no settings are loaded, so no log file is written.
    private static int StoreApiKey()
    {
        // Typing the key as an argument would leave it in the shell's history; only a pipe is read.
        var key = Console.IsInputRedirected ? SecretStore.ReadKey(Console.OpenStandardInput()) : "";
        if (string.IsNullOrWhiteSpace(key))
        {
            StartupError.Show($"Pipe the API key into Naad.exe {SetApiKeyArgument}, as docs/settings.md describes.");
            return 1;
        }

        if (!SecretStore.IsValidKey(key))
        {
            Log.Error(LogEvent.ApiKeyInvalid);
            StartupError.Show("That isn't a valid API key: it contains spaces or characters outside plain ASCII. " +
                "Nothing was saved.");
            return 1;
        }

        if (new SecretStore(SecretStore.DefaultPath).Save(key) is { } error)
        {
            Log.Error(LogEvent.ApiKeyStoreFailed, error);
            StartupError.Show("The API key could not be saved. The previously stored key, if any, is unchanged.");
            return 1;
        }

        Log.Info(LogEvent.ApiKeyStored);
        return 0;
    }

    // Settings are loaded after the single-instance check, so only one instance ever
    // migrates or rewrites the file. The log file starts only if settings allow it; until
    // then, lines go to the console only.
    private static SettingsLoadResult LoadSettings(SettingsStore store)
    {
        var result = store.Load();
        if (result.Settings.FileLogging)
        {
            StartFileLogging();
        }

        SettingsStore.LogOutcome(result);
        return result;
    }

    private static ApiKeyResult ResolveKey(SecretStore keys, bool log)
    {
        var resolved = keys.Resolve(Environment.GetEnvironmentVariable(SecretStore.OverrideVariable));
        if (log)
        {
            SecretStore.LogOutcome(resolved);
            _apiKey = resolved.Key;
        }

        return resolved;
    }

    private static void StartFileLogging()
    {
        try
        {
            Log.Configure(Log.DefaultDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Naad still works without a log file; lines keep going to the console.
            Console.Error.WriteLine($"Log file unavailable: {e.GetType().Name}");
        }
    }

    private static void HandleUnhandledExceptions()
    {
        // An exception in one dictation must not end the app: it is logged and the message
        // loop continues. This also replaces WinForms' default dialog, which shows the
        // exception message and could expose user content.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(LogEvent.UnhandledUiException, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception error)
            {
                Log.Error(LogEvent.UnhandledException, error);
            }
            else
            {
                Log.Error(LogEvent.UnhandledException);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(LogEvent.UnobservedTaskException, e.Exception);
            e.SetObserved();
        };
    }

    // Null when the chosen provider can't run yet; the shortcut then asks the user to finish setup.
    private static ITranscriber? CreateTranscriber(HttpClient http, string? apiKey, LocalModel model, AppSettings s) =>
        ProviderSelection.Choose(s.TranscriptionProvider, apiKey is not null, model.IsReady()) switch
        {
            TranscriptionProvider.Cloud => new OpenAiCompatibleTranscriber(
                http, apiKey!, s.TranscriptionBaseUrl, s.TranscriptionModel, s.Language, s.Vocabulary),
            TranscriptionProvider.Local => new LocalWhisperTranscriber(model.Path, s.Language, s.Vocabulary),
            _ => null,
        };

    // Every local function below runs on this UI thread: the hook, the tray, the controller's
    // events and the settings window all live on it.
    private static void Run(SettingsStore store, SettingsLoadResult loaded, SecretStore keys, ApiKeyResult key)
    {
        // The keyboard hook and the WinForms clipboard need this STA thread to pump messages.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settings = loaded.Settings;
        var apiKey = key.Key;

        // A newer Naad wrote settings.json; this build never replaces it (docs/settings.md).
        var canSave = loaded.Status != SettingsStatus.FutureVersion;

        // Settings saved during a dictation, applied once it ends.
        AppSettings? pending = null;
        SettingsWindow? window = null;
        Action? openSettings = null;
        var model = LocalModel.Base;

        // Not disposed at exit: a local transcription may still be running native code, and the
        // process ending frees the model anyway. A replaced one is disposed while idle.
        ITranscriber? transcriber = null;
        var sounds = settings.PlaySounds ? new SoundCues() : null;
        try
        {
            // Exit ends the message loop; the usings below then dispose in reverse order:
            // controller (ignore triggers, cancel transcription and paste), clipboard window, hook
            // (unhook), recorder (microphone), HTTP client, overlay, tray icon. The finally block then
            // disposes the sounds, and Main releases the single-instance mutex. The tray's actions
            // reach objects that exist by the time the menu can be clicked.
            DictationController? dictation = null;
            using var tray = new TrayIcon(
                settings.DictationShortcut,
                settings.HandsFree,
                copyLastTranscript: () => dictation?.CopyKeptTranscriptAsync() ?? Task.FromResult(false),
                openSettings: () => openSettings?.Invoke(),
                exit: Application.ExitThread);
            using var overlay = new RecordingOverlay();
            using var http = new HttpClient();
            using var recorder = new AudioRecorder();
            recorder.UseMicrophone(settings.Microphone);

            // Raised on the capture thread; the overlay is drawn on this one.
            var ui = SynchronizationContext.Current!;
            recorder.LevelMeasured += (rms, duration) => ui.Post(_ => overlay.ShowLevel(rms, duration), null);
            using var hook = new KeyboardHook(settings.DictationShortcut);
            using var clipboard = new WindowsClipboard();
            var inserter = new TextInserter(
                clipboard, TextInserter.AnyModifierDown, TextInserter.SendPaste, UnicodeTyping.Type);
            using var controller = new DictationController(
                recorder,
                transcriber = CreateTranscriber(http, apiKey, model, settings),
                inserter,
                FocusGuard.Foreground,
                FocusGuard.Check,
                TimeProvider.System);
            dictation = controller;
            controller.UseHandsFree(settings.HandsFree);
            var startup = StartupEntry.ForThisApp();
            controller.StateChanged += tray.ShowState;

            // Recording starts with focus on the window being dictated into, so the overlay
            // appears on that window's monitor.
            controller.StateChanged += state => overlay.ShowState(state, FocusGuard.Foreground().Handle);
            if (sounds is not null)
            {
                controller.StateChanged += sounds.OnStateChanged;
            }

            controller.StateChanged += state =>
            {
                if (state == DictationState.Idle && pending is { } next)
                {
                    Apply(next);
                }
            };
            controller.MicrophoneFailed += tray.ShowMicrophoneError;
            controller.NothingHeard += tray.ShowNothingHeard;
            controller.TranscriptionFailed += tray.ShowTranscriptionError;
            controller.TranscriptKept += tray.ShowTranscriptKept;
            controller.TranscriberMissing += tray.ShowApiKeyNeeded;

            // The hook raises these on this thread. Disposing the controller first makes later
            // events no-ops, and the hook is unhooked right after.
            hook.TriggerPressed += controller.Press;
            hook.TriggerReleased += controller.Release;
            hook.TriggerCancelled += controller.Cancel;
            hook.CaptureEnded += step => window?.ShowCapture(step);

            openSettings = OpenSettings;
            Log.Info(LogEvent.AppStarted);
            if (transcriber is null)
            {
                OpenSettings();
            }

            Application.Run();

            // Closed before the hook is unhooked, so a capture in progress ends cleanly.
            window?.Close();

            void OpenSettings()
            {
                if (window is not null)
                {
                    window.Activate();
                    return;
                }

                window = new SettingsWindow(
                    pending ?? settings,
                    ResolveKey(keys, log: false),
                    Microphones(),
                    startup.IsEnabled(),
                    model.IsReady(),
                    DownloadModelAsync,
                    TestKeyAsync,
                    Save,
                    beginCapture: () =>
                    {
                        // A capture swallows every key, so it never starts during a dictation.
                        if (controller.State != DictationState.Idle)
                        {
                            return false;
                        }

                        hook.BeginCapture();
                        return true;
                    },
                    hook.CancelCapture);
                window.FormClosed += (_, _) => window = null;
                window.Show();
                Log.Info(LogEvent.SettingsWindowOpened);
            }

            async Task<ModelDownloadResult> DownloadModelAsync(
                IProgress<double> progress, CancellationToken cancellationToken)
            {
                var started = Stopwatch.GetTimestamp();
                var result = await model.DownloadAsync(http, progress, cancellationToken);
                if (result.Outcome == ModelDownloadOutcome.Downloaded)
                {
                    Log.Info(
                        LogEvent.LocalModelDownloaded,
                        ("download_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
                }
                else
                {
                    Log.Warn(
                        LogEvent.LocalModelDownloadFailed, ("outcome", (int)result.Outcome), ("code", result.Code));
                }

                return result;
            }

            async Task<KeyTestResult> TestKeyAsync(string testedKey, Uri baseUrl, CancellationToken cancellationToken)
            {
                var result = await OpenAiCompatibleTranscriber.TestKeyAsync(
                    http, testedKey, baseUrl, cancellationToken);
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

            // Saves the key first, so a key that can't be stored changes nothing else. Settings that
            // can't be written still apply until Naad exits, and the window says so.
            string? Save(AppSettings next, string? newKey, bool startAtLogin)
            {
                if (newKey is not null)
                {
                    if (keys.Save(newKey) is { } keyError)
                    {
                        Log.Error(LogEvent.ApiKeyStoreFailed, keyError);
                        return "The API key couldn't be saved, so nothing was changed. Try again.";
                    }

                    Log.Info(LogEvent.ApiKeyStored);
                    apiKey = ResolveKey(keys, log: true).Key;
                }

                string? problem = null;
                // Written whenever ticked, so an entry left by a moved copy points here again.
                var startedAtLogin = startup.IsEnabled();
                if (startAtLogin || startedAtLogin)
                {
                    if (startup.Set(startAtLogin) is { } startupError)
                    {
                        Log.Error(LogEvent.StartAtLoginFailed, startupError);
                        problem = "Naad couldn't change whether it starts when you sign in. ";
                    }
                    else if (startAtLogin != startedAtLogin)
                    {
                        Log.Info(startAtLogin ? LogEvent.StartAtLoginEnabled : LogEvent.StartAtLoginDisabled);
                    }
                }

                if (!canSave)
                {
                    Log.Warn(LogEvent.SettingsSaveSkippedNewerFile);
                    problem += "A newer version of Naad wrote settings.json, so these changes apply only until " +
                        "Naad exits.";
                }
                else if (store.Save(next) is { } writeError)
                {
                    Log.Error(LogEvent.SettingsWriteFailed, writeError);
                    problem += "settings.json couldn't be written, so these changes apply only until Naad exits.";
                }
                else
                {
                    Log.Info(LogEvent.SettingsSaved);
                }

                Apply(next);
                return problem?.TrimEnd();
            }

            // A dictation keeps the settings it started with: during one, the change waits for Idle.
            void Apply(AppSettings next)
            {
                if (controller.State != DictationState.Idle)
                {
                    pending = next;
                    Log.Info(LogEvent.SettingsApplyDeferred);
                    return;
                }

                pending = null;
                if (next.FileLogging != settings.FileLogging)
                {
                    if (next.FileLogging)
                    {
                        StartFileLogging();
                    }
                    else
                    {
                        Log.StopFileLogging();
                    }
                }

                if (next.PlaySounds != settings.PlaySounds)
                {
                    if (sounds is not null)
                    {
                        controller.StateChanged -= sounds.OnStateChanged;
                        sounds.Dispose();
                    }

                    sounds = next.PlaySounds ? new SoundCues() : null;
                    if (sounds is not null)
                    {
                        controller.StateChanged += sounds.OnStateChanged;
                    }
                }

                hook.SetShortcut(next.DictationShortcut);
                tray.SetShortcut(next.DictationShortcut, next.HandsFree);
                controller.UseHandsFree(next.HandsFree);
                recorder.UseMicrophone(next.Microphone);
                var replaced = transcriber;
                transcriber = CreateTranscriber(http, apiKey, model, next);
                controller.UseTranscriber(transcriber);

                // Idle, so nothing is using it: frees a loaded local model's memory at once.
                (replaced as IDisposable)?.Dispose();
                settings = next;
                Log.Info(LogEvent.SettingsApplied);
            }
        }
        finally
        {
            sounds?.Dispose();
        }
    }

    private static IReadOnlyList<string> Microphones()
    {
        try
        {
            return AudioRecorder.DeviceNames();
        }
        catch (MmException e)
        {
            // A device vanished mid-list; the window still offers the Windows default.
            Log.Error(LogEvent.MicrophoneUnavailable, e);
            return [];
        }
    }
}
