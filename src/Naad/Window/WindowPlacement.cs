using Naad.Tray;

namespace Naad.Window;

/// <summary>Where the main window was on one monitor, in screen pixels.</summary>
/// <param name="Monitor">Windows' device name for the monitor, such as \\.\DISPLAY2.</param>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
internal sealed record SavedWindow(string Monitor, int X, int Y, int Width, int Height)
{
    // Smaller than this is unusable; larger than a 16K screen is a damaged file.
    public const int MinSize = 200;
    public const int MaxSize = 16_384;

    // Monitors can sit left of or above the primary one: four 16K screens in a row is beyond any desk.
    private const long MaxOffset = MaxSize * 4L;

    /// <summary>A size Naad could have saved, on a monitor name Windows could report.</summary>
    public bool IsValid => Settings.AppSettings.IsValidMonitor(Monitor)
        && Width is >= MinSize and <= MaxSize && Height is >= MinSize and <= MaxSize
        && Math.Abs((long)X) <= MaxOffset && Math.Abs((long)Y) <= MaxOffset;
}

/// <summary>Remembers the main window's place on each monitor, and keeps it on screen. Pure.</summary>
internal static class WindowPlacement
{
    /// <summary>One per monitor; more than this are monitors long gone.</summary>
    public const int MaxSaved = 8;

    /// <summary>
    /// Where to open the window: its place on the monitor under the mouse, else its last place on a
    /// monitor still connected, fitted to that monitor's working area. Null to let Windows choose.
    /// </summary>
    public static Rectangle? Fit(
        IReadOnlyList<SavedWindow> saved, IReadOnlyList<OverlayScreen> screens, string? mouseMonitor)
    {
        var connected = saved.Where(s => screens.Any(screen => screen.Name == s.Monitor)).ToList();
        var chosen = connected.FirstOrDefault(s => s.Monitor == mouseMonitor) ?? connected.FirstOrDefault();
        if (chosen is null)
        {
            return null;
        }

        var area = screens.First(s => s.Name == chosen.Monitor).WorkingArea;
        return Clamp(new Rectangle(chosen.X, chosen.Y, chosen.Width, chosen.Height), area);
    }

    /// <summary><paramref name="rect"/> shrunk to fit <paramref name="area"/>, then moved inside it.</summary>
    public static Rectangle Clamp(Rectangle rect, Rectangle area)
    {
        var width = Math.Min(rect.Width, area.Width);
        var height = Math.Min(rect.Height, area.Height);
        var x = Math.Clamp(rect.X, area.Left, area.Right - width);
        var y = Math.Clamp(rect.Y, area.Top, area.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    /// <summary>The saved places with <paramref name="next"/> first, replacing that monitor's old one.</summary>
    public static IReadOnlyList<SavedWindow> Remember(IReadOnlyList<SavedWindow> saved, SavedWindow next) =>
        [next, .. saved.Where(s => s.Monitor != next.Monitor).Take(MaxSaved - 1)];
}
