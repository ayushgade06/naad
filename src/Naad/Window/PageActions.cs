using Naad.History;
using Naad.Meetings;
using Naad.Snippets;

namespace Naad.Window;

/// <summary>What the History, Dictionary and Snippets pages do; every member runs on the UI thread.</summary>
/// <param name="History">The dictation history.</param>
/// <param name="Copy">Puts text on the clipboard, out of clipboard history.</param>
/// <param name="Paste">Pastes text into the window that has focus once Naad's window is out of the way.</param>
/// <param name="Retry">Transcribes a record's recording again and copies the result; null if it failed.</param>
/// <param name="Words">The dictionary in use.</param>
/// <param name="SaveWords">Saves a new dictionary; returns what went wrong, or null.</param>
/// <param name="Snippets">The saved snippets.</param>
/// <param name="SnippetsChanged">Called after the snippets are saved, so dictation uses them.</param>
/// <param name="Meetings">Meeting notes: recording, and the saved meetings.</param>
internal sealed record PageActions(
    HistoryRecorder History,
    Func<string, Task<bool>> Copy,
    Func<string, Task<bool>> Paste,
    Func<HistoryRecord, Task<string?>> Retry,
    Func<IReadOnlyList<string>> Words,
    Func<IReadOnlyList<string>, string?> SaveWords,
    SnippetStore Snippets,
    Action SnippetsChanged,
    MeetingActions Meetings);
