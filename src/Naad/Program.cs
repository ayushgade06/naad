using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Transcription;

namespace Naad;

internal static class Program
{
    // ponytail: read from the environment for the slice; P12 stores the key with DPAPI.
    private const string ApiKeyVariable = "NAAD_GROQ_API_KEY";

    [STAThread]
    private static int Main()
    {
        // First, before any hook or microphone work: a second instance must leave without
        // registering a second keyboard hook. Released when Main returns, on this thread.
        using var instance = SingleInstanceGuard.TryAcquire();
        if (instance is null)
        {
            Console.WriteLine("Naad is already running.");
            return 0;
        }

        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StartupError.Show(
                $"{ApiKeyVariable} is not set. Set it to a Groq API key (https://console.groq.com/keys), " +
                "then start Naad again.");
            return 1;
        }

        try
        {
            Run(apiKey);
            return 0;
        }
        catch (Exception e)
        {
            StartupError.Show($"Naad stopped because of an error.\n\n{StartupError.Describe(e, apiKey)}");
            return 1;
        }
    }

    private static void Run(string apiKey)
    {
        // The keyboard hook and the WinForms clipboard need this STA thread to pump messages.
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            ui.Post(_ => Application.ExitThread(), null);
        };

        using var http = new HttpClient();
        using var recorder = new AudioRecorder();
        using var hook = new KeyboardHook();
        new DictationController(hook, recorder, new OpenAiCompatibleTranscriber(http, apiKey)).Listen();

        Console.WriteLine("Naad vertical slice. Hold Right Ctrl, speak, release. Press Ctrl+C here to quit.");
        Application.Run();
    }
}
