using System.Security;
using Microsoft.Win32;

namespace Naad.Window;

/// <summary>Light or dark windows; System follows the Windows app mode.</summary>
internal enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <param name="Background">The window.</param>
/// <param name="Surface">Panels and the navigation list.</param>
/// <param name="Text">Body text.</param>
/// <param name="Muted">Hints and secondary text.</param>
/// <param name="Accent">The selection and buttons that matter; Naad's teal.</param>
/// <param name="Border">Lines between areas.</param>
internal sealed record Palette(uint Background, uint Surface, uint Text, uint Muted, uint Accent, uint Border);

/// <summary>Which palette Naad's windows use. Pure apart from <see cref="WindowsUsesLightApps"/>.</summary>
internal static class Theme
{
    public static Palette Light { get; } = new(0xFFF7F7F8, 0xFFFFFFFF, 0xFF1C1C1E, 0xFF6B6B70, 0xFF0F9D8A, 0xFFE2E2E6);

    public static Palette Dark { get; } = new(0xFF1C1C1E, 0xFF26262A, 0xFFF2F2F4, 0xFFA0A0A8, 0xFF2DD4BF, 0xFF3A3A40);

    public static bool IsDark(ThemeChoice choice, bool windowsUsesLightApps) =>
        choice == ThemeChoice.Dark || (choice == ThemeChoice.System && !windowsUsesLightApps);

    public static Palette For(ThemeChoice choice, bool windowsUsesLightApps) =>
        IsDark(choice, windowsUsesLightApps) ? Dark : Light;

    /// <summary>
    /// Windows' "Choose your app mode" switch, AppsUseLightTheme under the user's Personalize key.
    /// Light when it can't be read, as on Windows versions before the switch existed.
    /// </summary>
    public static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
