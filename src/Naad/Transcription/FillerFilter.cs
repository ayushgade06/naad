using System.Text;

namespace Naad.Transcription;

/// <summary>
/// Drops transcripts that are only a phrase Whisper is known to invent for noise or silence,
/// learned from the video subtitles it was trained on. Pure and deliberately conservative: a
/// transcript is dropped only when all of it is one of these phrases, and text that holds
/// anything else is never changed.
/// </summary>
internal static class FillerFilter
{
    // Whisper's best-known inventions that people almost never dictate on their own.
    // "Thank you." is left out on purpose: it is Whisper's most common invention, but also a
    // real thing to say, and SilenceGate already keeps the silence that causes it from upload.
    // ponytail: whole-transcript matches only; a filler appended to real speech ("Hello.
    // Thanks for watching!") passes through. Trim trailing fillers if users report them.
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "you",
        "thanks for watching",
        "thank you for watching",
        "thank you so much for watching",
        "please subscribe",
        "like and subscribe",
        "subtitles by the amaraorg community",
        "transcription by castingwords",
    };

    /// <summary>
    /// Whether the whole transcript is a filler phrase, ignoring case, punctuation and
    /// spacing: "Thanks for watching!" and "thanks  for watching" both match. A transcript
    /// that is only a non-speech tag, such as "[BLANK_AUDIO]", is one too.
    /// </summary>
    public static bool IsFiller(string transcript) =>
        IsNonSpeechTag(transcript) || Fillers.Contains(Normalize(transcript));

    /// <summary>
    /// Whether the text is a single bracketed annotation, such as "[BLANK_AUDIO]", "[Music]" or
    /// "(silence)". whisper.cpp writes these for sounds that aren't speech, as segments of
    /// their own; dictated text is never wholly in brackets.
    /// </summary>
    public static bool IsNonSpeechTag(string text)
    {
        var t = text.Trim();
        return t.Length >= 2
            && ((t[0] == '[' && t.IndexOf(']') == t.Length - 1) || (t[0] == '(' && t.IndexOf(')') == t.Length - 1));
    }

    private static string Normalize(string text)
    {
        var words = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                words.Append(char.ToLowerInvariant(c));
            }
            else if (char.IsWhiteSpace(c) && words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().TrimEnd();
    }
}
