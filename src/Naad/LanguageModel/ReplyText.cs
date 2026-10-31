namespace Naad.LanguageModel;

/// <summary>Turns a model's raw reply into the text it meant to give. Pure.</summary>
internal static class ReplyText
{
    private const string ThinkOpen = "<think>";
    private const string ThinkClose = "</think>";

    /// <summary>
    /// Drops reasoning blocks (<c>&lt;think&gt;…&lt;/think&gt;</c>), which Qwen3, DeepSeek and
    /// similar models write before their answer, then trims. An unclosed block means the model ran
    /// out of tokens while still reasoning, so everything from it on is dropped.
    /// </summary>
    public static string Clean(string reply)
    {
        var text = reply;
        int open;
        while ((open = text.IndexOf(ThinkOpen, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var close = text.IndexOf(ThinkClose, open, StringComparison.OrdinalIgnoreCase);
            text = close < 0 ? text[..open] : text[..open] + text[(close + ThinkClose.Length)..];
        }

        // Some servers strip the opening tag but keep the closing one.
        var orphan = text.IndexOf(ThinkClose, StringComparison.OrdinalIgnoreCase);
        if (orphan >= 0)
        {
            text = text[(orphan + ThinkClose.Length)..];
        }

        return text.Trim();
    }
}
