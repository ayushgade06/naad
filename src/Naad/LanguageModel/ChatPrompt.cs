namespace Naad.LanguageModel;

/// <summary>One request to a language model: what it is told to do, and the text to work on.</summary>
/// <param name="Instructions">What the model is told to do: the system message.</param>
/// <param name="Text">The text to work on, such as a transcript: user content, never logged.</param>
/// <param name="MaxTokens">The longest reply wanted, in tokens.</param>
internal sealed record ChatPrompt(string Instructions, string Text, int MaxTokens = ChatPrompt.DefaultMaxTokens)
{
    // A minute of dictation is about 150 words, some 200 tokens; this leaves room for several
    // minutes without letting a model that rambles run on for long.
    public const int DefaultMaxTokens = 1024;

    // The generated ToString would print the user's text into any log or exception that used it.
    public override string ToString() => $"ChatPrompt {{ InstructionChars = {Instructions.Length}, TextChars = {Text.Length} }}";
}
