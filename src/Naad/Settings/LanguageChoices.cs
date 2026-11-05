using System.Globalization;

namespace Naad.Settings;

/// <summary>
/// The spoken-language list in Settings: "Detect automatically" and common languages shown by
/// name with their code, "English (en)". Any other code can still be typed. Pure.
/// </summary>
internal static class LanguageChoices
{
    public const string Detect = "Detect automatically";

    // Languages Whisper and the cloud services transcribe well; the rest can be typed as codes.
    private static readonly string[] Codes =
    [
        "en", "hi", "mr", "bn", "ta", "te", "gu", "kn", "ml", "pa", "ur", "es", "fr", "de", "it", "pt", "nl",
        "ru", "uk", "pl", "cs", "sv", "da", "no", "fi", "el", "tr", "ar", "he", "fa", "zh", "ja", "ko", "id",
        "ms", "vi", "th", "ro", "hu",
    ];

    /// <summary>What the list offers, <see cref="Detect"/> first.</summary>
    public static IReadOnlyList<string> All { get; } = [Detect, .. Codes.Select(Label)];

    /// <summary>How <paramref name="code"/> shows in the list; <see cref="Detect"/> for none.</summary>
    public static string Label(string? code)
    {
        if (code is null)
        {
            return Detect;
        }

        try
        {
            return $"{CultureInfo.GetCultureInfo(code).EnglishName} ({code})";
        }
        catch (CultureNotFoundException)
        {
            // A code Windows doesn't name still works for the transcriber; show it as typed.
            return code;
        }
    }

    /// <summary>
    /// The code in what the user picked or typed: "English (en)" and "en" give "en"; blank or
    /// <see cref="Detect"/> give "", which detects it.
    /// </summary>
    public static string Code(string text)
    {
        var trimmed = text.Trim();
        if (trimmed == Detect)
        {
            return "";
        }

        var open = trimmed.LastIndexOf('(');
        return open >= 0 && trimmed.EndsWith(')') ? trimmed[(open + 1)..^1].Trim() : trimmed;
    }
}
