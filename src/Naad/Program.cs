using Naad.Audio;
using Naad.Cleanup;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Settings;
using Naad.Transcription;
using Naad.Tray;
using Naad.Updates;
using Naad.Window;
using NAudio;
using Velopack;

namespace Naad;

internal static class Program
{
    // Stores the API key piped to standard input, then exits; see docs/settings.md.
    private const string SetApiKeyArgument = "--set-api-key";

    // Every key resolved this session, kept only to redact them from the fatal-error dialog.
    private static readonly HashSet<string> Keys = [];

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
            return SetApiKeyCommand.Run(SetApiKeyArgument);
        }

        // First, before any hook or microphone work: a second instance must leave without
        // registering a second keyboard hook. Released when Main returns, on this thread.
        using var instance = SingleInstanceGuard.TryAcquire();
        if (instance is null)
        {
            // Console only: the running instance owns the settings and the log file. Starting
            // Naad again opens the running one's window, the way back when its icon is hidden.
            Log.Info(LogEvent.SecondInstanceRejected);
            ShowWindowSignal.Send();
            return 0;
        }

        var store = new SettingsStore(SettingsStore.DefaultPath);
        var loaded = LoadSettings(store);
        var keys = ServiceKeys.ForThisPc(loaded.Settings.TranscriptionService);

        try
        {
            // Without a usable key Naad still starts, and opens its settings window to ask for one.
            Run(store, loaded, keys, background: args.Contains(StartupEntry.BackgroundArgument));
            Log.Info(LogEvent.AppStopped);
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.FatalError, e);
            var described = StartupError.Describe(e, null);
            foreach (var key in Keys)
            {
                described = StartupError.Redact(described, key);
            }

            StartupError.Show($"Naad stopped because of an error.\n\n{described}");
            return 1;
        }
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

    // Logs where the transcription key comes from, and keeps it to redact from error dialogs.
    private static void ResolveTranscriptionKey(ServiceKeys keys, AppSettings s)
    {
        var resolved = keys.Resolve(s.TranscriptionService);
        SecretStore.LogOutcome(resolved);
        Remember(resolved.Key);
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

    // Every local function below runs on this UI thread: the hook, the tray, the controller's
    // events and the settings window all live on it.
    private static void Run(SettingsStore store, SettingsLoadResult loaded, ServiceKeys keys, bool background)
    {
        // The keyboard hook and the WinForms clipboard need this STA thread to pump messages.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settings = loaded.Settings;
        ResolveTranscriptionKey(keys, settings);
        string? KeyFor(CloudService service) => Remember(keys.Resolve(service).Key);
        var temperature = new TemperatureSupport(TemperatureSupport.DefaultPath, TimeProvider.System);
        var cooldowns = new RateLimitCooldowns(RateLimitCooldowns.DefaultPath, TimeProvider.System);

        // Not disposed at exit, like the transcriber: a local model may still be running native code.
        ILanguageModel? languageModel = null;

        // A newer Naad wrote settings.json; this build never replaces it (docs/settings.md).
        var canSave = loaded.Status != SettingsStatus.FutureVersion;

        // Offline, a cloud choice falls back to this PC; the user is told once per session.
        var offlineNoticeShown = false;
        Action? offline = null;

        // Settings saved during a dictation, applied once it ends.
        AppSettings? pending = null;
        var replaceLanguageModel = false;
        WindowHost? windows = null;

        // Not disposed at exit: a local transcription may still be running native code, and the
        // process ending frees the model anyway. A replaced one is disposed while idle.
        ITranscriber? transcriber = null;
        var sounds = Sounds(settings);
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
                openSettings: () => windows?.Open(Pages.Settings),
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
                transcriber = TranscriberSetup.Create(http, KeyFor, settings, Offline),
                inserter,
                FocusGuard.Foreground,
                FocusGuard.Check,
                TimeProvider.System);
            dictation = controller;
            controller.UseHandsFree(settings.HandsFree);
            UseLanguageModel(settings, replace: true);
            var startup = StartupEntry.ForThisApp();
            var actions = new SettingsActions(store, keys, startup, canSave, http);
            offline = () =>
            {
                Log.Info(LogEvent.OfflineFallbackUsed);
                if (!offlineNoticeShown)
                {
                    offlineNoticeShown = true;
                    tray.ShowOfflineFallback();
                }
            };
            controller.StateChanged += tray.ShowState;

            // Recording starts with focus on the window being dictated into, so the overlay
            // appears on that window's monitor.
            controller.StateChanged += state => overlay.ShowState(state, FocusGuard.Foreground().Handle);
            controller.CleaningUp += overlay.ShowCleaningUp;
            controller.CleanupFellBack += overlay.ShowCleanupFellBack;
            controller.MicrophoneOpened += () => PlayIf(settings.PlaySounds, s => s.PlayStart());
            controller.MicrophoneClosed += () => PlayIf(settings.PlaySounds, s => s.PlayStop());
            controller.MicrophoneFailed += _ => PlayIf(settings.ErrorSound, s => s.PlayError());
            controller.TranscriptionFailed += _ => PlayIf(settings.ErrorSound, s => s.PlayError());
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
            hook.CaptureEnded += step => windows?.SettingsForm?.ShowCapture(step);
            windows = new WindowHost(
                () => pending ?? settings, Microphones, startup.IsEnabled, WindowActions, Application.ExitThread);
            tray.AddItems(
                ToggleDictation,
                Microphones,
                () => (pending ?? settings).Microphone,
                microphone => Save(new(
                    (pending ?? settings) with { Microphone = microphone }, new Dictionary<CloudService, string>(),
                    startup.IsEnabled())),
                () => windows.Open(Pages.Home),
                windows.OpenSetup);
            tray.SetVisible(settings.ShowTrayIcon);

            // Raised on a thread-pool thread when Naad is started again.
            using var showWindow = new ShowWindowSignal(() => ui.Post(_ => windows.Open(null), null));

            // Only an installed copy updates, and only when the user clicks the offer.
            using var updates = new UpdatePrompt(
                new AppUpdater(), tray, () => controller.State == DictationState.Idle, Application.ExitThread);

            Log.Info(LogEvent.AppStarted);

            // A fresh install gets the setup guide; one that can't dictate yet gets Settings.
            if (loaded.Status == SettingsStatus.Missing)
            {
                windows.OpenSetup();
            }
            else if (transcriber is null || (!settings.ShowTrayIcon && !background))
            {
                windows.Open(transcriber is null ? Pages.Settings : Pages.Home);
            }

            Application.Run();

            // Closed before the hook is unhooked, so a capture in progress ends cleanly.
            windows.CloseAll();

            // From the tray: a menu click is neither a press nor a hold, so hands-free's press-twice
            // rule stops it, and a held dictation ends with a release.
            void ToggleDictation()
            {
                if (controller.State == DictationState.Recording && !settings.HandsFree)
                {
                    controller.Release();
                }
                else
                {
                    controller.Press();
                }
            }

            void PlayIf(bool wanted, Action<SoundCues> play)
            {
                if (wanted && sounds is { } cues)
                {
                    play(cues);
                }
            }

            // A new model only when its settings or key changed, or none could run before (a model
            // downloaded since): replacing a model on this PC unloads it.
            void UseLanguageModel(AppSettings s, bool replace)
            {
                if (replace || languageModel is null)
                {
                    // Disposing waits for a model still loading, so it runs off this thread.
                    if (languageModel is IDisposable old)
                    {
                        _ = Task.Run(old.Dispose);
                    }

                    languageModel = CleanupSetup.CreateModel(http, KeyFor, s, temperature, Offline);
                }

                controller.UseCleanup(
                    CleanupSetup.Create(languageModel, http, KeyFor, s, temperature, cooldowns));
            }

            void Offline() => offline?.Invoke();

            SettingsWindowActions WindowActions() => new(
                keys.Resolve,
                actions.DownloadModelAsync,
                actions.TestKeyAsync,
                async (onScreen, typedKey, token) =>
                {
                    var key = typedKey ?? KeyFor(onScreen.TranscriptionService);
                    using var tested = TranscriberSetup.CreateForTest(http, key, onScreen) as IDisposable;
                    return await SettingsActions.TestTranscriptionAsync(tested as ITranscriber, token);
                },
                (instructions, sample, token) =>
                    CleanupSetup.TestAsync(languageModel, settings, instructions, sample, token),
                cooldowns.For,
                Save,
                BeginCapture: () =>
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

            // Keys first, so a key that can't be stored changes nothing else.
            string? Save(SettingsChange change)
            {
                var keyError = actions.SaveKeys(change);

                // Even after a failure: a key may have been stored before it.
                ResolveTranscriptionKey(keys, change.Settings);
                var languageModelKeyChanged = change.NewKeys.ContainsKey(change.Settings.LanguageModelService);
                if (keyError is not null)
                {
                    return keyError;
                }

                var problem = actions.SaveSettings(change);
                Apply(change.Settings, languageModelKeyChanged);
                return problem;
            }

            // A dictation keeps the settings it started with: during one, the change waits for Idle.
            void Apply(AppSettings next, bool languageModelKeyChanged = false)
            {
                if (controller.State != DictationState.Idle)
                {
                    pending = next;
                    replaceLanguageModel |= languageModelKeyChanged;
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

                if ((next.PlaySounds || next.ErrorSound, next.SoundVolume)
                    != (settings.PlaySounds || settings.ErrorSound, settings.SoundVolume))
                {
                    sounds?.Dispose();
                    sounds = Sounds(next);
                }

                tray.SetVisible(next.ShowTrayIcon);

                hook.SetShortcut(next.DictationShortcut);
                tray.SetShortcut(next.DictationShortcut, next.HandsFree);
                controller.UseHandsFree(next.HandsFree);
                recorder.UseMicrophone(next.Microphone);
                var replaced = transcriber;
                transcriber = TranscriberSetup.Create(http, KeyFor, next, Offline);
                controller.UseTranscriber(transcriber);
                UseLanguageModel(next, replaceLanguageModel || languageModelKeyChanged || ModelChanged(settings, next));
                replaceLanguageModel = false;

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

    private static string? Remember(string? key)
    {
        if (key is not null)
        {
            lock (Keys)
            {
                Keys.Add(key);
            }
        }

        return key;
    }

    private static bool ModelChanged(AppSettings before, AppSettings after) =>
        before.LanguageModelProvider != after.LanguageModelProvider
        || before.LocalLanguageModel != after.LocalLanguageModel
        || before.LanguageModelService != after.LanguageModelService
        || before.LanguageModelBaseUrl != after.LanguageModelBaseUrl
        || before.LanguageModelName != after.LanguageModelName
        || before.LanguageModelTimeoutSeconds != after.LanguageModelTimeoutSeconds;

    // Only built when some tone is on: each holds its tones in memory.
    private static SoundCues? Sounds(AppSettings s) =>
        s.PlaySounds || s.ErrorSound ? new SoundCues(s.SoundVolume) : null;

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
