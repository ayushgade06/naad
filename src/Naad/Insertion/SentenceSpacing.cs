namespace Naad.Insertion;

/// <summary>
/// The space after a finished sentence, added to text going into an app so the next dictation
/// doesn't run into it ("end.Next"). Only the inserted text gets it; history keeps the text as
/// said. Pure.
/// </summary>
internal static class SentenceSpacing
{
    /// <summary><paramref name="text"/> with a space after it when it ends in . ! or ?.</summary>
    public static string AfterSentence(string text) =>
        text.Length > 0 && text[^1] is '.' or '!' or '?' ? text + " " : text;
}
