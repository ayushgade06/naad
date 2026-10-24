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
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine(
                $"Set {ApiKeyVariable} to a Groq API key (https://console.groq.com/keys), then run Naad again.");
            return 1;
        }

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
        return 0;
    }
}
