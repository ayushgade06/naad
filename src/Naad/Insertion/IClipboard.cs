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
    /// history and cloud sync. Returns the sequence number after the write, or null on failure;
    /// a failure may leave the clipboard empty.
    /// </summary>
    Task<uint?> SetTextAsync(string text);

    /// <summary>Puts a snapshot back. Returns false if the clipboard couldn't be opened or written.</summary>
    Task<bool> RestoreAsync(ClipboardSnapshot snapshot);
}
