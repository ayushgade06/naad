using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Settings;
using Naad.Transcription;
using Naad.Tray;

namespace Naad;

internal static class Program
{
    // Stores the API key piped to standard input, then exits; see docs/settings.md.
    private const string SetApiKeyArgument = "--set-api-key";

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        HandleUnhandledExceptions();

        // Needs no single-instance check: it only writes the key file, which a running
        // instance read at its start and never reads again.
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

        var settings = ApplySettings();

        var resolved = new SecretStore(SecretStore.DefaultPath)
            .Resolve(Environment.GetEnvironmentVariable(SecretStore.OverrideVariable));
        SecretStore.LogOutcome(resolved);
        if (resolved.Key is not { } apiKey)
        {
            StartupError.Show(StartupError.ApiKeyProblem(resolved.Status));
            return 1;
        }

        try
        {
            Run(apiKey, settings);
            Log.Info(LogEvent.AppStopped);
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.FatalError, e);
            StartupError.Show($"Naad stopped because of an error.\n\n{StartupError.Describe(e, apiKey)}");
            return 1;
        }
    }

    // Console only: no settings are loaded, so no log file is written.
    private static int StoreApiKey()
    {
        // Typing the key as an argument would leave it in the shell's history; only a pipe is read.
        var key = Console.IsInputRedirected ? Console.In.ReadToEnd() : "";
        if (string.IsNullOrWhiteSpace(key))
        {
            StartupError.Show($"Pipe the API key into Naad.exe {SetApiKeyArgument}, as docs/settings.md describes.");
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
    private static AppSettings ApplySettings()
    {
        var result = new SettingsStore(SettingsStore.DefaultPath).Load();
        if (result.Settings.FileLogging)
        {
            StartFileLogging();
        }

        SettingsStore.LogOutcome(result);
        return result.Settings;
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

    private static void Run(string apiKey, AppSettings settings)
    {
        // The keyboard hook and the WinForms clipboard need this STA thread to pump messages.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // Exit ends the message loop; the usings below then dispose in reverse order:
        // controller (stop triggers, cancel transcription), clipboard window, hook (unhook),
        // recorder (microphone), HTTP client, tray icon. Main then releases the single-instance mutex.
        using var tray = new TrayIcon(settings.DictationShortcut, exit: Application.ExitThread);
        using var http = new HttpClient();
        using var recorder = new AudioRecorder();
        using var hook = new KeyboardHook(settings.DictationShortcut);
        var transcriber = new OpenAiCompatibleTranscriber(
            http, apiKey, settings.TranscriptionBaseUrl, settings.TranscriptionModel);
        using var clipboard = new WindowsClipboard();
        var inserter = new TextInserter(clipboard, TextInserter.AnyModifierDown, TextInserter.SendPaste);
        using var controller = new DictationController(hook, recorder, transcriber, inserter);
        controller.MicrophoneFailed += tray.ShowMicrophoneError;
        controller.Listen();

        Log.Info(LogEvent.AppStarted);
        Application.Run();
    }
}
