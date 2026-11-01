namespace Naad.Tray;

/// <summary>How the recording overlay looks.</summary>
internal enum OverlayStyle
{
    /// <summary>The pill with voice bars and words such as "Transcribing".</summary>
    Pill,

    /// <summary>A small capsule of bars only; notices still use the full pill.</summary>
    Compact,
}

/// <summary>A monitor, as the overlay's placement sees it.</summary>
/// <param name="Name">Windows' device name, such as \\.\DISPLAY2.</param>
/// <param name="WorkingArea">The monitor's area without the taskbar.</param>
/// <param name="Primary">Whether it is the primary monitor.</param>
internal readonly record struct OverlayScreen(string Name, Rectangle WorkingArea, bool Primary);

/// <summary>Where the overlay goes and how big it is. Pure: decided from numbers alone.</summary>
internal static class OverlayLayout
{
    /// <summary>The <c>overlayMonitor</c> value for the primary monitor; null follows the active window.</summary>
    public const string PrimaryMonitor = "Primary";

    // Sizes at 96 DPI: big enough to read at a glance, small enough to cover little.
    private const int PillWidth = 132;
    private const int PillHeight = 36;
    private const int CompactWidth = 64;
    private const int CompactHeight = 22;

    // Room for the stop button at the pill's right end in hands-free mode.
    private const int StopRoom = 28;
    private const int StopDiameter = 18;

    // With live preview the pill widens to a line of about ten words above the bars.
    private const int LiveWidth = 380;
    private const int LiveTextHeight = 24;

    /// <summary>
    /// The working area the overlay is centred in: the active window's monitor (null choice), the
    /// primary one, or the named one. A monitor that isn't connected now falls back to the
    /// primary, as does an active window that is on none.
    /// </summary>
    /// <param name="screens">The connected monitors.</param>
    /// <param name="active">The name of the active window's monitor, or null.</param>
    /// <param name="choice">The <c>overlayMonitor</c> setting.</param>
    public static Rectangle PickArea(IReadOnlyList<OverlayScreen> screens, string? active, string? choice)
    {
        var wanted = choice is null ? active : choice == PrimaryMonitor ? null : choice;
        foreach (var screen in screens)
        {
            if (wanted is not null && screen.Name == wanted)
            {
                return screen.WorkingArea;
            }
        }

        return screens.FirstOrDefault(s => s.Primary).WorkingArea is { IsEmpty: false } primary
            ? primary
            : screens.Count > 0 ? screens[0].WorkingArea : Rectangle.Empty;
    }

    /// <summary>
    /// The overlay's size at 96 DPI. Words (a notice, a label or live text) always get the pill,
    /// so they stay readable; the compact style shows bars only.
    /// </summary>
    /// <param name="style">The chosen style.</param>
    /// <param name="words">Whether it shows words rather than bars.</param>
    /// <param name="live">Whether live text shows above the bars.</param>
    /// <param name="stopButton">Whether the hands-free stop button shows.</param>
    public static Size Size(OverlayStyle style, bool words, bool live, bool stopButton)
    {
        var size = live ? new Size(LiveWidth, PillHeight + LiveTextHeight)
            : style == OverlayStyle.Compact && !words ? new Size(CompactWidth, CompactHeight)
            : new Size(PillWidth, PillHeight);
        return stopButton ? size with { Width = size.Width + StopRoom } : size;
    }

    /// <summary>The stop button, a circle at the right end of <paramref name="overlay"/>.</summary>
    /// <param name="overlay">The overlay's bounds, in pixels.</param>
    /// <param name="scale">Pixels per logical pixel.</param>
    public static Rectangle StopButton(Rectangle overlay, double scale)
    {
        var diameter = (int)Math.Round(StopDiameter * scale);
        var bottomBand = (int)Math.Round(Math.Min(overlay.Height / scale, PillHeight) * scale);
        var right = (int)Math.Round((StopRoom - StopDiameter) / 2.0 * scale);
        return new Rectangle(
            overlay.Right - right - diameter,
            overlay.Bottom - bottomBand + ((bottomBand - diameter) / 2),
            diameter,
            diameter);
    }
}
