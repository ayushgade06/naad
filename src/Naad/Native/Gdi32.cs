using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>gdi32.dll declarations: the memory device context the recording overlay draws through.</summary>
internal static partial class Gdi32
{
    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);
}
