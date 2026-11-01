namespace Naad.Insertion;

/// <summary>One clipboard format's bytes, copied out of its global memory block.</summary>
internal sealed record ClipboardFormatData(uint Format, byte[] Data);

/// <summary>
/// A copy of what the clipboard held before Naad wrote a transcript to it: every format whose
/// data is a plain memory block, which covers text, RTF, HTML, files copied in Explorer and
/// images (as device-independent bitmaps). Formats that are GDI handles or private to the app
/// that copied them can't be copied byte for byte; they are counted in <see cref="Skipped"/>.
/// </summary>
/// <param name="Formats">The copied formats, in the clipboard's order. Empty for an empty clipboard.</param>
/// <param name="Skipped">How many formats could not be copied, and so won't come back.</param>
internal sealed record ClipboardSnapshot(IReadOnlyList<ClipboardFormatData> Formats, int Skipped)
{
    public static ClipboardSnapshot Empty { get; } = new([], 0);

    // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE and CF_ENHMETAFILE hold GDI handles, and the
    // CF_DSP* variants are owner-specific. Windows synthesizes CF_BITMAP from CF_DIB, which is
    // copied, so images still come back.
    private static readonly HashSet<uint> HandleFormats = [2, 3, 9, 14, 0x0080, 0x0082, 0x0083, 0x008E];

    // CF_PRIVATEFIRST..CF_PRIVATELAST and CF_GDIOBJFIRST..CF_GDIOBJLAST: their handles mean
    // something only to the app that put them there, and Windows frees them when it empties.
    private const uint PrivateFirst = 0x0200;
    private const uint GdiObjectLast = 0x03FF;

    /// <summary>Whether a format's data is a memory block that can be copied and put back.</summary>
    public static bool IsCopyable(uint format) =>
        !HandleFormats.Contains(format) && format is not (>= PrivateFirst and <= GdiObjectLast);

    // The generated ToString would print every byte array's type; contents never matter in a log.
    public override string ToString() => $"ClipboardSnapshot {{ Formats = {Formats.Count}, Skipped = {Skipped} }}";

    // CF_UNICODETEXT: UTF-16 with a terminating null.
    private const uint UnicodeTextFormat = 13;

    /// <summary>The plain text on the clipboard, or null when it holds none.</summary>
    public string? Text => Formats.FirstOrDefault(f => f.Format == UnicodeTextFormat)?.Data is { } data
        ? System.Text.Encoding.Unicode.GetString(data).TrimEnd('\0')
        : null;
}
