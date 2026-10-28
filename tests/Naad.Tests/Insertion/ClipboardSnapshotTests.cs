using Naad.Insertion;

namespace Naad.Tests.Insertion;

public sealed class ClipboardSnapshotTests
{
    // Standard formats (winuser.h) whose data is a plain memory block, plus a registered one.
    [Theory]
    [InlineData(1u)] // CF_TEXT
    [InlineData(7u)] // CF_OEMTEXT
    [InlineData(8u)] // CF_DIB
    [InlineData(13u)] // CF_UNICODETEXT
    [InlineData(15u)] // CF_HDROP: files copied in Explorer
    [InlineData(16u)] // CF_LOCALE
    [InlineData(17u)] // CF_DIBV5
    [InlineData(0xC0FFu)] // a registered format, such as "HTML Format" or "Rich Text Format"
    public void IsCopyable_MemoryBlockFormat_ReturnsTrue(uint format) =>
        Assert.True(ClipboardSnapshot.IsCopyable(format));

    // GDI handles, owner-display formats and private handle ranges can't be copied byte for byte.
    [Theory]
    [InlineData(2u)] // CF_BITMAP
    [InlineData(3u)] // CF_METAFILEPICT
    [InlineData(9u)] // CF_PALETTE
    [InlineData(14u)] // CF_ENHMETAFILE
    [InlineData(0x80u)] // CF_OWNERDISPLAY
    [InlineData(0x82u)] // CF_DSPBITMAP
    [InlineData(0x83u)] // CF_DSPMETAFILEPICT
    [InlineData(0x8Eu)] // CF_DSPENHMETAFILE
    [InlineData(0x200u)] // CF_PRIVATEFIRST
    [InlineData(0x2FFu)] // CF_PRIVATELAST
    [InlineData(0x300u)] // CF_GDIOBJFIRST
    [InlineData(0x3FFu)] // CF_GDIOBJLAST
    public void IsCopyable_HandleOrPrivateFormat_ReturnsFalse(uint format) =>
        Assert.False(ClipboardSnapshot.IsCopyable(format));

    [Fact]
    public void ToString_OmitsData()
    {
        var snapshot = new ClipboardSnapshot([new ClipboardFormatData(13, "s\0e\0c\0r\0e\0t\0"u8.ToArray())], 1);

        Assert.Equal("ClipboardSnapshot { Formats = 1, Skipped = 1 }", snapshot.ToString());
    }
}
