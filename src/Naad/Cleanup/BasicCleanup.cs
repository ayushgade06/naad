using System.Text;
using System.Text.RegularExpressions;

namespace Naad.Cleanup;

/// <summary>
/// Rule-based cleanup that needs no model and takes no time: drops hesitation sounds and a word
/// said twice in a row, starts with a capital letter and ends with a full stop. Pure.
/// </summary>
internal static partial class BasicCleanup
{
    // Words that are legitimately doubled in English ("he had had", "that that is"). Numbers and
    // words separated by a comma ("no, no, no") are never collapsed.
    private static readonly HashSet<string> Doubled = new(StringComparer.OrdinalIgnoreCase) { "had", "that" };

    public static string Clean(string transcript)
    {
        var text = Filler().Replace(transcript, " ");
        text = RepeatedWord().Replace(text, m => Doubled.Contains(m.Groups[1].Value) ? m.Value : m.Groups[1].Value);
        text = Spaces().Replace(text, " ");
        text = SpaceBeforePunctuation().Replace(text, "$1");
        text = text.Trim().TrimStart(',', ';', ' ');
        if (text.Length == 0)
        {
            return "";
        }

        return Finish(text);
    }

    /// <summary>
    /// Only a capital first letter and a final full stop, keeping everything else, line breaks
    /// included: for text a language model already tidied.
    /// </summary>
    public static string Finish(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        var result = new StringBuilder(trimmed);
        result[0] = char.ToUpper(result[0], System.Globalization.CultureInfo.CurrentCulture);
        if (char.IsLetterOrDigit(result[^1]))
        {
            result.Append('.');
        }

        return result.ToString();
    }

    // Hesitation sounds as whole words, with a comma or ellipsis Whisper put after them.
    [GeneratedRegex(@"\b(?:u+m+|u+h+m*|e+r+m+|e+r+|a+h+|h+m+|m{2,})\b[,.…]*", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    [GeneratedRegex(@"\b(\p{L}+)(?:[ \t]+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedWord();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s+([,.;:!?])")]
    private static partial Regex SpaceBeforePunctuation();
}
