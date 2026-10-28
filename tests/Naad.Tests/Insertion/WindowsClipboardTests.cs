using System.Text;
using Naad.Insertion;
using Naad.Native;

namespace Naad.Tests.Insertion;

/// <summary>
/// Runs the real Windows clipboard, so it is explicit: it never runs in CI or in a plain
/// dotnet test. It briefly replaces the clipboard of whoever runs it, then puts it back, which
/// is the behaviour under test. After dotnet build, run it with:
/// tests/Naad.Tests/bin/Debug/net10.0-windows/Naad.Tests.exe -class Naad.Tests.Insertion.WindowsClipboardTests
/// -explicit only -showLiveOutput
/// </summary>
public sealed class WindowsClipboardTests
{
    private const uint UnicodeText = 13;
    private const string Synthetic = "naad clipboard harness text";

    [Fact(Explicit = true)]
    public async Task SetThenRestore_RealClipboard_RoundTripsEveryCopiedFormat()
    {
        using var clipboard = new WindowsClipboard();
        var original = await clipboard.CaptureAsync();
        Assert.NotNull(original);

        uint? written;
        ClipboardSnapshot? during;
        try
        {
            written = await clipboard.SetTextAsync(Synthetic);
            during = await clipboard.CaptureAsync();
        }
        finally
        {
            Assert.True(await clipboard.RestoreAsync(original));
        }

        var restored = await clipboard.CaptureAsync();

        // The transcript was written, alone, with the privacy markers that keep it out of history.
        Assert.NotNull(written);
        Assert.NotNull(during);
        var text = Assert.Single(during.Formats, f => f.Format == UnicodeText);
        Assert.Equal(Synthetic + "\0", Encoding.Unicode.GetString(text.Data));
        Assert.Contains(during.Formats, f => f.Format == Format("CanIncludeInClipboardHistory"));
        Assert.Contains(during.Formats, f => f.Format == Format("ExcludeClipboardContentFromMonitorProcessing"));

        // Every format that was copied comes back byte for byte. Only counts are reported.
        Assert.NotNull(restored);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"copied formats: {original.Formats.Count}, skipped formats: {original.Skipped}");
        foreach (var format in original.Formats)
        {
            var back = restored.Formats.FirstOrDefault(f => f.Format == format.Format);
            Assert.True(back is not null, $"format {format.Format} missing after restore");
            Assert.True(back.Data.AsSpan().SequenceEqual(format.Data), $"format {format.Format} changed");
        }
    }

    [Fact(Explicit = true)]
    public async Task SequenceNumber_AfterAnotherWrite_Changes()
    {
        using var clipboard = new WindowsClipboard();
        var original = await clipboard.CaptureAsync();
        Assert.NotNull(original);
        try
        {
            var written = await clipboard.SetTextAsync(Synthetic);
            await clipboard.SetTextAsync(Synthetic);

            Assert.NotEqual(written, clipboard.SequenceNumber);
        }
        finally
        {
            Assert.True(await clipboard.RestoreAsync(original));
        }
    }

    private static uint Format(string name) => User32.RegisterClipboardFormat(name);
}
