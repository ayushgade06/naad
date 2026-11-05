using Naad.Cleanup;
using Naad.History;

namespace Naad.Window;

/// <summary>The words the History page uses for a record's state. Pure.</summary>
internal static class HistoryDetails
{
    /// <summary>How cleanup went, in words.</summary>
    public static string Cleanup(CleanupOutcome? outcome) => outcome switch
    {
        null => "Not recorded (this dictation is older than the record of it).",
        CleanupOutcome.Unchanged => "Off: pasted as it was said.",
        CleanupOutcome.Basic => "Basic: tidied by rules on this PC.",
        CleanupOutcome.Cleaned => "Cleaned by the language model.",
        CleanupOutcome.Empty => "The language model found nothing worth pasting.",
        CleanupOutcome.FellBack => "The language model failed or was too slow, so the words went in as said.",
        CleanupOutcome.Edited => "The selected text was rewritten.",
        CleanupOutcome.EditFailed => "The rewrite failed, so the selected text was left as it was.",
        CleanupOutcome.Snippet => "A snippet's trigger: its text was pasted exactly, without cleanup.",
        _ => outcome.Value.ToString(),
    };

    /// <summary>What a record's text box says when there is no text to show.</summary>
    public static string? Placeholder(HistoryRecord record) => record.Outcome switch
    {
        HistoryOutcome.Failed when record.Selection is not null => "The edit failed. Retry to try it again.",
        HistoryOutcome.Failed => "No transcript. Retry to transcribe the recording again.",
        HistoryOutcome.Empty => "Nothing worth pasting was heard. Retry to transcribe the recording again.",
        _ => null,
    };

    /// <summary>How a retry ends: kept with its text, empty, or failed when an edit failed again.</summary>
    public static HistoryOutcome RetryOutcome(CleanupResult cleaned) =>
        cleaned.Outcome == CleanupOutcome.EditFailed ? HistoryOutcome.Failed
            : cleaned.Text.Length == 0 ? HistoryOutcome.Empty
            : HistoryOutcome.Kept;

    /// <summary>Whether the record has text to copy, paste or add to the dictionary.</summary>
    public static bool HasText(HistoryRecord record) =>
        record.Outcome is HistoryOutcome.Pasted or HistoryOutcome.Kept;
}
