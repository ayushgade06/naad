using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>dwmapi.dll declarations.</summary>
internal static partial class Dwmapi
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE: Windows 10 20H1 and later draw a dark title bar when it is 1.
    private const int UseImmersiveDarkMode = 20;

    /// <summary>Asks Windows to draw <paramref name="window"/>'s title bar dark or light. Best effort.</summary>
    public static void SetDarkTitleBar(nint window, bool dark)
    {
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(window, UseImmersiveDarkMode, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
