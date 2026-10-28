using Naad.Settings;

namespace Naad;

/// <summary>
/// Reports errors that stop Naad. A tray app has no console, so without a dialog a failed
/// start would be invisible.
/// </summary>
internal static class StartupError
{
    private const string Redacted = "[redacted]";

    private const string HowToStoreKey =
        "Store a Groq API key (https://console.groq.com/keys) by piping it into Naad.exe --set-api-key, " +
        "as docs/settings.md describes, then start Naad again.";

    /// <summary>What to tell the user when no usable API key was found. Never includes the key.</summary>
    public static string ApiKeyProblem(SecretStatus status) => status switch
    {
        SecretStatus.Missing => $"No API key is stored. {HowToStoreKey}",
        _ => "The stored API key can't be read. It may be damaged, or another Windows user saved it. " +
            HowToStoreKey,
    };

    /// <summary>Describes an exception for the user with <paramref name="secret"/> removed.</summary>
    public static string Describe(Exception error, string? secret) =>
        Redact($"{error.GetType().Name}: {error.Message}", secret);

    public static string Redact(string text, string? secret) =>
        string.IsNullOrEmpty(secret) ? text : text.Replace(secret, Redacted, StringComparison.Ordinal);

    /// <summary>Shows a blocking error dialog. Never pass unredacted secrets.</summary>
    public static void Show(string message)
    {
        Console.Error.WriteLine(message);
        MessageBox.Show(message, "Naad", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
