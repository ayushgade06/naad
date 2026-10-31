using System.Text.RegularExpressions;

namespace Naad.Cleanup;

internal enum ReplyVerdict
{
    /// <summary>The reply is the cleaned text.</summary>
    Accepted,

    /// <summary>The model said the dictation held nothing: paste nothing.</summary>
    Empty,

    /// <summary>The reply was blank.</summary>
    Blank,

    /// <summary>The reply starts like a chat answer ("Sure, here is…").</summary>
    Preamble,

    /// <summary>The reply is far longer than what was said.</summary>
    TooLong,

    /// <summary>The model seems to have answered or carried out the dictation instead of tidying it.</summary>
    Answered,
}

/// <param name="Verdict">What the reply is.</param>
/// <param name="Text">For Accepted, the text to paste; otherwise empty.</param>
internal readonly record struct ReplyCheck(ReplyVerdict Verdict, string Text = "")
{
    // The generated ToString would print the text.
    public override string ToString() => $"ReplyCheck {{ Verdict = {Verdict}, Chars = {Text.Length} }}";
}

/// <summary>
/// Decides whether a model's reply can replace the transcript. Small models sometimes answer a
/// dictated question, or wrap the text in chat phrases; those replies are refused, so the
/// transcript is pasted instead. Pure.
/// </summary>
internal static partial class CleanupReplyCheck
{
    // A cleanup removes words. Translations run longer, so they get more room; the slack covers
    // short dictations, where one added word is a large share.
    private const double MaxGrowth = 1.6;
    private const double MaxTranslationGrowth = 3.0;
    private const int GrowthSlack = 40;

    // Below this share of the transcript's words, a reply is about something else.
    private const double MinOverlap = 0.35;

    // A model may call only a short dictation empty ("okay", "hmm right"): one that says EMPTY to
    // more has misread its instructions, and the transcript is kept.
    private const int MaxWordsForEmpty = 3;

    private static readonly string[] Preambles =
    [
        "sure", "certainly", "of course", "here is", "here's", "here are", "okay, here", "ok, here",
        "the cleaned", "cleaned text", "corrected text", "i'd be happy", "i would be happy", "as an ai",
    ];

    // Words that suggest the dictation is addressed to an AI or asks for writing.
    private static readonly HashSet<string> Markers = new(StringComparer.OrdinalIgnoreCase)
    {
        "write", "draft", "ask", "tell", "summarize", "summarise", "explain", "answer", "reply", "respond",
        "generate", "create", "translate", "chatgpt", "claude", "gpt", "ai", "assistant", "please",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "to", "of", "in", "on", "at", "for", "with", "is", "are", "was",
        "were", "be", "it", "this", "that", "i", "you", "we", "they", "he", "she", "me", "my", "your", "our",
        "so", "um", "uh", "do", "can", "could", "would", "should", "will", "just", "like", "about",
    };

    /// <param name="transcript">What was said.</param>
    /// <param name="reply">The model's reply, without reasoning blocks.</param>
    /// <param name="translating">Whether the model was asked to write in another language.</param>
    /// <param name="guard">Whether to refuse replies that look like answers (the instruction guard).</param>
    public static ReplyCheck Check(string transcript, string reply, bool translating, bool guard)
    {
        var text = Unquote(reply.Trim());
        var said = BasicCleanup.Clean(transcript);
        if (text.TrimEnd('.').Equals(CleanupPrompt.EmptyReply, StringComparison.Ordinal))
        {
            return new(Words(said).Count() <= MaxWordsForEmpty ? ReplyVerdict.Empty : ReplyVerdict.Blank);
        }

        if (text.Length == 0)
        {
            return new(ReplyVerdict.Blank);
        }

        if (StartsWithPreamble(text) && !StartsWithPreamble(said))
        {
            return new(ReplyVerdict.Preamble);
        }

        var growth = translating ? MaxTranslationGrowth : MaxGrowth;
        if (text.Length > (transcript.Length * growth) + GrowthSlack)
        {
            return new(ReplyVerdict.TooLong);
        }

        // An answer drops most of what was said ("What's the capital of France?" becomes "Paris"),
        // or a request's own words ("write", "ask Claude"). A translation shares no words, so it is
        // never judged this way.
        var markers = Words(transcript).Where(Markers.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (guard && !translating
            && (Overlap(transcript, text) < MinOverlap || (markers.Count > 0 && !Words(text).Any(markers.Contains))))
        {
            return new(ReplyVerdict.Answered);
        }

        return new(ReplyVerdict.Accepted, text);
    }

    // One pair of straight or curly double quotes around the whole reply.
    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] is '"' or '“' && text[^1] is '"' or '”' ? text[1..^1].Trim() : text;

    // A whole phrase: "Sure, …" but not "Surely" or "Suresh".
    private static bool StartsWithPreamble(string text) => Preambles.Any(p =>
        text.StartsWith(p, StringComparison.OrdinalIgnoreCase)
        && (text.Length == p.Length || !char.IsLetterOrDigit(text[p.Length])));

    // The share of the transcript's content words that the reply kept.
    private static double Overlap(string transcript, string reply)
    {
        var said = Words(transcript).Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (said.Count == 0)
        {
            return 1;
        }

        var kept = Words(reply).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return said.Count(kept.Contains) / (double)said.Count;
    }

    private static IEnumerable<string> Words(string text) => Word().Matches(text).Select(m => m.Value);

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex Word();
}
