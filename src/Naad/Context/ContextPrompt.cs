using Naad.LanguageModel;

namespace Naad.Context;

/// <summary>
/// What the context model is told: a two-sentence account of what the user is doing, from what
/// Naad read of the app and, when allowed, a screenshot of it. The account is only a hint for
/// cleanup. Pure.
/// </summary>
internal static class ContextPrompt
{
    /// <summary>
    /// When <see cref="DefaultInstructions"/> last changed, as yyyy-MM-dd. A custom prompt based
    /// on an older default gets a notice offering the new one; bump it with every change.
    /// </summary>
    public const string DefaultVersion = "2026-11-01";

    public const string DefaultInstructions = """
        You help a speech-to-text app understand what someone is about to dictate. You get what is
        known about the app they are typing in, and sometimes a screenshot of its window.

        Reply with exactly two plain sentences: what the person is doing, and what they are most
        likely about to write. Mention concrete details that help spell words right: for an email,
        who it is to and its subject; for code or a terminal, the file, language or command; for a
        document or chat, its topic and the names in it. When something isn't clear, say you are
        unsure instead of guessing. No labels, lists, quotes or Markdown.
        """;

    // Two sentences of names and topics fit well within this.
    private const int MaxTokens = 200;

    // Enough text around the cursor to tell the topic; the cleanup prompt gets more of it.
    private const int NearbyCharacters = 600;

    /// <summary>The prompt for <paramref name="context"/>, with its screenshot when it has one.</summary>
    /// <param name="customInstructions">The user's own instructions, or null for the default.</param>
    /// <param name="context">What was read from the app: user content, never logged.</param>
    public static ChatPrompt Build(string? customInstructions, DictationContext context)
    {
        var instructions = string.IsNullOrWhiteSpace(customInstructions) ? DefaultInstructions : customInstructions;
        var nearby = context.NearbyText is { } text
            ? ContextReader.Around(text, text.Length / 2, NearbyCharacters)
            : null;
        var details = string.Join(
            "\n",
            "App: " + (context.App ?? "unknown"),
            "Window title: " + (context.WindowTitle ?? "not read"),
            "Text near the cursor: " + (nearby is null ? "not read" : "\n<<<\n" + nearby + "\n>>>"));
        var ask = context.Screenshot is null
            ? "What is the person doing? Two sentences."
            : "What is the person doing in this window? Two sentences.";
        return new(instructions, ask + "\n\n" + details, MaxTokens, context.Screenshot);
    }

    /// <summary>
    /// The model's reply as at most two sentences on one line, or null when nothing is left:
    /// a reply that runs on would crowd the cleanup prompt.
    /// </summary>
    public static string? TwoSentences(string reply)
    {
        var text = string.Join(' ', reply.Split((char[])['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        var end = -1;
        for (var sentence = 0; sentence < 2; sentence++)
        {
            var next = text.IndexOfAny(['.', '!', '?'], end + 1);
            if (next < 0)
            {
                return text.Length == 0 ? null : text;
            }

            end = next;
        }

        return text[..(end + 1)];
    }
}
