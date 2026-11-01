namespace Naad.Context;

/// <summary>
/// What Naad read from the app being dictated into, once, when the dictation started. User
/// content: it goes only into the language model's prompt (and, for an edit, its history record),
/// never into the log.
/// </summary>
/// <param name="App">The app's process name, such as "notepad"; null when unknown.</param>
/// <param name="NearbyText">Up to 2,000 characters around the caret, as spelling hints; null when not read.</param>
/// <param name="Selection">The selected text when this dictation is an edit; null otherwise.</param>
/// <param name="Screenshot">A PNG of the window, only when the user opted in for a cloud model.</param>
/// <param name="NeedsSelection">Manual Edit Mode was asked for, but nothing was selected.</param>
/// <param name="WindowTitle">The window's title, only when the user turned that on; null otherwise.</param>
/// <param name="Summary">The context model's two sentences on what the user is doing; null for none.</param>
/// <param name="Invocation">How an edit was asked for; null for a dictation.</param>
internal sealed record DictationContext(
    string? App,
    string? NearbyText,
    string? Selection,
    byte[]? Screenshot,
    bool NeedsSelection = false,
    string? WindowTitle = null,
    string? Summary = null,
    EditInvocation? Invocation = null)
{
    public static DictationContext None { get; } = new(null, null, null, null);

    /// <summary>Whether the dictation is an instruction for the selected text.</summary>
    public bool IsEdit => Selection is not null;

    // The generated ToString would print the text.
    public override string ToString() =>
        $"DictationContext {{ Nearby = {NearbyText?.Length ?? 0}, Selection = {Selection?.Length ?? 0}, " +
        $"Title = {WindowTitle?.Length ?? 0}, Summary = {Summary?.Length ?? 0} }}";
}
