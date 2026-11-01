using System.Drawing;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class OverlayLayoutTests
{
    private static readonly OverlayScreen Main = new(@"\\.\DISPLAY1", new(0, 0, 1920, 1040), Primary: true);
    private static readonly OverlayScreen Side = new(@"\\.\DISPLAY2", new(1920, 0, 1280, 984), Primary: false);
    private static readonly OverlayScreen[] Screens = [Main, Side];

    [Fact]
    public void PickArea_ActiveWindow_FollowsItsMonitor() =>
        Assert.Equal(Side.WorkingArea, OverlayLayout.PickArea(Screens, Side.Name, choice: null));

    [Fact]
    public void PickArea_Primary_IgnoresTheActiveWindow() =>
        Assert.Equal(Main.WorkingArea, OverlayLayout.PickArea(Screens, Side.Name, OverlayLayout.PrimaryMonitor));

    [Fact]
    public void PickArea_NamedMonitor_UsesIt() =>
        Assert.Equal(Side.WorkingArea, OverlayLayout.PickArea(Screens, Main.Name, Side.Name));

    [Theory]
    [InlineData(@"\\.\DISPLAY9")]
    [InlineData(null)]
    public void PickArea_MonitorGone_FallsBackToPrimary(string? choice) =>
        Assert.Equal(Main.WorkingArea, OverlayLayout.PickArea(Screens, active: null, choice ?? "unplugged"));

    [Fact]
    public void PickArea_NoMonitors_IsEmpty() =>
        Assert.True(OverlayLayout.PickArea([], active: null, choice: null).IsEmpty);

    [Fact]
    public void Size_LiveWithStopButton_KeepsTheLiveLineAndAddsTheButton() =>
        Assert.True(
            OverlayLayout.Size(OverlayStyle.Compact, false, live: true, stopButton: true).Width
            > OverlayLayout.Size(OverlayStyle.Pill, false, live: true, stopButton: false).Width);

    [Fact]
    public void Size_CompactBars_IsSmallerThanThePill()
    {
        var pill = OverlayLayout.Size(OverlayStyle.Pill, words: false, live: false, stopButton: false);
        var compact = OverlayLayout.Size(OverlayStyle.Compact, words: false, live: false, stopButton: false);

        Assert.True(compact.Width < pill.Width && compact.Height < pill.Height);
    }

    [Fact]
    public void Size_CompactWithWords_UsesThePillSoTheyStayReadable() =>
        Assert.Equal(
            OverlayLayout.Size(OverlayStyle.Pill, words: true, live: false, stopButton: false),
            OverlayLayout.Size(OverlayStyle.Compact, words: true, live: false, stopButton: false));

    [Fact]
    public void Size_StopButton_WidensToMakeRoom() =>
        Assert.True(
            OverlayLayout.Size(OverlayStyle.Pill, false, false, stopButton: true).Width
            > OverlayLayout.Size(OverlayStyle.Pill, false, false, stopButton: false).Width);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void StopButton_Overlay_IsACircleInsideItsRightEnd(double scale)
    {
        var overlay = new Rectangle(100, 900, (int)(160 * scale), (int)(36 * scale));

        var button = OverlayLayout.StopButton(overlay, scale);

        Assert.Equal(button.Width, button.Height);
        Assert.True(overlay.Contains(button));
        Assert.True(button.Left > overlay.Left + (overlay.Width / 2));
    }
}
