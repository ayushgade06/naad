using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Settings;
using Naad.Transcription;
using Naad.Tray;

namespace Naad;

internal static class Program
{
    // ponytail: read from the environment for the slice; P12 stores the key with DPAPI.
    private const string ApiKeyVariable = "NAAD_GROQ_API_KEY";

    [STAThread]
    private static int Main()
    {
        ApplicationConfiguration.Initialize();
        HandleUnhandledExceptions();

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

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Error(LogEvent.ApiKeyMissing);
            StartupError.Show(
                $"{ApiKeyVariable} is not set. Set it to a Groq API key (https://console.groq.com/keys), " +
                "then start Naad again.");
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
        // controller (stop triggers, cancel transcription), hook (unhook), recorder
        // (microphone), HTTP client, tray icon. Main then releases the single-instance mutex.
        using var tray = new TrayIcon(settings.DictationShortcut, exit: Application.ExitThread);
        using var http = new HttpClient();
        using var recorder = new AudioRecorder();
        using var hook = new KeyboardHook(settings.DictationShortcut);
        using var controller = new DictationController(hook, recorder, new OpenAiCompatibleTranscriber(http, apiKey));
        controller.MicrophoneFailed += tray.ShowMicrophoneError;
        controller.Listen();

        Log.Info(LogEvent.AppStarted);
        Application.Run();
    }
}
