namespace Naad.Insertion;

/// <summary>
/// The Windows clipboard, behind a seam so <see cref="TextInserter"/>'s restore policy can be
/// tested without touching the real one. Call on the UI thread.
/// </summary>
internal interface IClipboard
{
    /// <summary>
    /// Changes every time anything writes to the clipboard, so a changed value means someone
    /// else wrote after Naad did.
    /// </summary>
    uint SequenceNumber { get; }

    /// <summary>Copies the clipboard, or returns null if it couldn't be opened.</summary>
    Task<ClipboardSnapshot?> CaptureAsync();

    /// <summary>
    /// Replaces the clipboard with <paramref name="text"/>, marked to stay out of clipboard
    /// history and cloud sync unless <paramref name="keepOutOfHistory"/> is false. Returns the
    /// sequence number after the write, or null on failure; a failure may leave the clipboard empty.
    /// </summary>
    Task<uint?> SetTextAsync(string text, bool keepOutOfHistory = true);

    /// <summary>
    /// Puts a snapshot back, always kept out of clipboard history (it is there already). With
    /// <paramref name="ifUnchanged"/>, only while the clipboard still holds that write: checked once
    /// the clipboard is open, so nothing copied in the meantime is overwritten. Returns null when it
    /// had changed, false if the clipboard couldn't be opened or written.
    /// </summary>
    Task<bool?> RestoreAsync(ClipboardSnapshot snapshot, uint? ifUnchanged = null);
}
