using System.Text.RegularExpressions;

namespace Naad.Snippets;

/// <summary>A snippet: a spoken trigger and the text it expands into. User content.</summary>
/// <param name="Trigger">What is said, such as "my address".</param>
/// <param name="Text">What is pasted instead.</param>
internal sealed record Snippet(string Trigger, string Text)
{
    /// <summary>A new row, empty until the user fills it in.</summary>
    public static Snippet Blank { get; } = new("", "");

    /// <summary>Both a trigger and a text are filled in; anything less is never saved.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(Trigger) && !string.IsNullOrWhiteSpace(Text);

    // The generated ToString would print the text.
    public override string ToString() => $"Snippet {{ TriggerChars = {Trigger.Length}, TextChars = {Text.Length} }}";
}

/// <summary>Snippet expansion and the "press enter" voice command. Pure.</summary>
internal static partial class TranscriptCommands
{
    /// <summary>
    /// Replaces each spoken trigger with its text. A trigger matches whole words, ignoring case
    /// and the punctuation cleanup put around it; where triggers overlap, the longest wins.
    /// </summary>
    public static string Expand(string text, IReadOnlyList<Snippet> snippets)
    {
        // One pass, longest trigger first, so a snippet's own text is never expanded again.
        var usable = snippets
            .Select(s => (Words: Words(s.Trigger), s.Text))
            .Where(s => s.Words.Length > 0)
            .OrderByDescending(s => s.Words.Length)
            .ToList();
        if (usable.Count == 0)
        {
            return text;
        }

        // Words in order, with any spaces or punctuation between them; a full stop the cleanup
        // added right after the trigger goes with it.
        var alternatives = usable.Select((s, i) =>
            $"(?<s{i}>{string.Join(@"[\s\p{P}]+", s.Words.Select(Regex.Escape))})");
        var pattern = @"(?<![\p{L}\p{N}])(?:" + string.Join('|', alternatives) + @")(?![\p{L}\p{N}])[.!?]?";
        try
        {
            return Regex.Replace(
                text,
                pattern,
                m => usable[Enumerable.Range(0, usable.Count).First(i => m.Groups[$"s{i}"].Success)].Text,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (RegexMatchTimeoutException)
        {
            // Many long triggers over a long text: the words go in unexpanded rather than not at all.
            return text;
        }
    }

    /// <summary>
    /// The snippet whose trigger is the whole transcript, ignoring case and punctuation, or null.
    /// Checked on the raw transcript, so cleanup can't reword a trigger said on its own.
    /// </summary>
    public static Snippet? WholeMatch(string raw, IReadOnlyList<Snippet> snippets)
    {
        var key = TriggerKey(raw);
        return key.Length == 0 ? null : snippets.FirstOrDefault(s => s.IsComplete && TriggerKey(s.Trigger) == key);
    }

    /// <summary>A trigger as it is matched: its words, lowercase, one space apart.</summary>
    public static string TriggerKey(string trigger) => string.Join(' ', Words(trigger)).ToLowerInvariant();

    /// <summary>The triggers used by more than one snippet, as written the first time.</summary>
    public static IReadOnlyList<string> DuplicateTriggers(IEnumerable<Snippet> snippets) =>
        [.. snippets.Where(s => s.IsComplete)
            .GroupBy(s => TriggerKey(s.Trigger))
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Trigger.Trim())];

    /// <summary>
    /// A trailing "press enter" (any case, with punctuation around it) is taken off the text and
    /// reported, for Enter to be pressed after the paste.
    /// </summary>
    public static (string Text, bool PressEnter) PressEnter(string text)
    {
        var match = TrailingPressEnter().Match(text);
        return match.Success ? (text[..match.Index].TrimEnd(' ', ',', ';'), true) : (text, false);
    }

    private static string[] Words(string phrase) =>
        Regex.Split(phrase.Trim(), @"[\s\p{P}]+").Where(w => w.Length > 0).ToArray();

    [GeneratedRegex(@"[\s,;:]*\bpress\s+enter[\s.!]*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingPressEnter();
}
