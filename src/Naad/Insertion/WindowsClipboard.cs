using System.Runtime.InteropServices;
using System.Text;
using Naad.Native;

namespace Naad.Insertion;

/// <summary>
/// The Win32 clipboard. Create, use and dispose it on the UI thread: it owns a hidden
/// message-only window, which the clipboard needs as its owner and which must be pumped.
/// </summary>
internal sealed class WindowsClipboard : IClipboard, IDisposable
{
    private const uint UnicodeTextFormat = 13;

    // Another app (a clipboard manager, Office) may hold the clipboard open for a moment.
    // Ten tries 20 ms apart give it 200 ms, then the paste is given up rather than waited on.
    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(20);

    // Bounds the memory a snapshot can take; anything beyond is skipped, not copied.
    private const long MaxSnapshotBytes = 64L * 1024 * 1024;

    // Documented by Windows: data carrying these formats is left out of clipboard history
    // (Win+V), cloud clipboard sync and clipboard monitors. Their value is a DWORD 0.
    private static readonly uint[] PrivacyFormats =
    [
        User32.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"),
        User32.RegisterClipboardFormat("CanIncludeInClipboardHistory"),
        User32.RegisterClipboardFormat("CanUploadToCloudClipboard"),
    ];

    private readonly NativeWindow _owner = new();

    public WindowsClipboard()
    {
        // HWND_MESSAGE (-3): a window that is never shown and only receives messages.
        _owner.CreateHandle(new CreateParams { Parent = -3 });
    }

    public uint SequenceNumber => User32.GetClipboardSequenceNumber();

    public async Task<ClipboardSnapshot?> CaptureAsync()
    {
        if (!await OpenAsync())
        {
            return null;
        }

        try
        {
            var formats = new List<ClipboardFormatData>();
            var skipped = 0;
            long total = 0;
            for (var format = User32.EnumClipboardFormats(0); format != 0; format = User32.EnumClipboardFormats(format))
            {
                var data = ClipboardSnapshot.IsCopyable(format) ? Copy(User32.GetClipboardData(format)) : null;
                if (data is null || total + data.Length > MaxSnapshotBytes)
                {
                    skipped++;
                    continue;
                }

                total += data.Length;
                formats.Add(new(format, data));
            }

            return new(formats, skipped);
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    public async Task<uint?> SetTextAsync(string text)
    {
        var utf16 = Encoding.Unicode.GetBytes(text + "\0");
        return await WriteAsync([new(UnicodeTextFormat, utf16)]) ? SequenceNumber : null;
    }

    public Task<bool> RestoreAsync(ClipboardSnapshot snapshot) => WriteAsync(snapshot.Formats);

    public void Dispose() => _owner.DestroyHandle();

    // Every write also carries the privacy formats. On a restore that keeps the user's own data
    // from appearing twice in clipboard history.
    private async Task<bool> WriteAsync(IReadOnlyList<ClipboardFormatData> formats)
    {
        if (!await OpenAsync())
        {
            return false;
        }

        try
        {
            if (!User32.EmptyClipboard())
            {
                return false;
            }

            var ok = true;
            foreach (var format in formats)
            {
                ok &= Set(format.Format, format.Data);
            }

            foreach (var format in PrivacyFormats)
            {
                ok &= format == 0 || Set(format, new byte[sizeof(uint)]);
            }

            return ok;
        }
        finally
        {
            User32.CloseClipboard();
        }
    }

    private async Task<bool> OpenAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            if (User32.OpenClipboard(_owner.Handle))
            {
                return true;
            }

            if (attempt == OpenAttempts)
            {
                return false;
            }

            await Task.Delay(OpenRetryDelay);
        }
    }

    // Null when the handle isn't a readable memory block.
    private static byte[]? Copy(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        var size = Kernel32.GlobalSize(handle);
        if (size == 0 || size > MaxSnapshotBytes)
        {
            return null;
        }

        var source = Kernel32.GlobalLock(handle);
        if (source == 0)
        {
            return null;
        }

        try
        {
            var data = new byte[(int)size];
            Marshal.Copy(source, data, 0, data.Length);
            return data;
        }
        finally
        {
            Kernel32.GlobalUnlock(handle);
        }
    }

    private static bool Set(uint format, byte[] data)
    {
        // GlobalAlloc refuses 0 bytes; an empty block is stored as one zero byte.
        var block = Kernel32.GlobalAlloc(Kernel32.GmemMoveable, (nuint)Math.Max(1, data.Length));
        if (block == 0)
        {
            return false;
        }

        var target = Kernel32.GlobalLock(block);
        if (target == 0)
        {
            Kernel32.GlobalFree(block);
            return false;
        }

        Marshal.Copy(data, 0, target, data.Length);
        Kernel32.GlobalUnlock(block);

        // On success the clipboard owns the block; on failure it is still Naad's to free.
        if (User32.SetClipboardData(format, block) == 0)
        {
            Kernel32.GlobalFree(block);
            return false;
        }

        return true;
    }
}
