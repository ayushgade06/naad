using System.Drawing;
using System.Text.Json.Nodes;
using Naad.Settings;
using Naad.Tray;
using Naad.Window;

namespace Naad.Tests.Window;

public sealed class WindowPlacementTests
{
    private const string Left = "DISPLAY1";
    private const string Right = "DISPLAY2";

    private static readonly OverlayScreen[] Screens =
    [
        new(Left, new Rectangle(0, 0, 1920, 1040), Primary: true),
        new(Right, new Rectangle(1920, 0, 1280, 984), Primary: false),
    ];

    [Fact]
    public void Fit_MouseMonitorSaved_UsesItsPlace()
    {
        SavedWindow[] saved = [new(Left, 100, 100, 900, 700), new(Right, 2000, 50, 800, 600)];

        Assert.Equal(new Rectangle(2000, 50, 800, 600), WindowPlacement.Fit(saved, Screens, Right));
    }

    [Fact]
    public void Fit_MouseMonitorNotSaved_UsesTheNewestConnected()
    {
        SavedWindow[] saved = [new("DISPLAY9", 0, 0, 900, 700), new(Left, 100, 100, 900, 700)];

        Assert.Equal(new Rectangle(100, 100, 900, 700), WindowPlacement.Fit(saved, Screens, Right));
    }

    [Fact]
    public void Fit_MonitorGone_LetsWindowsChoose()
    {
        Assert.Null(WindowPlacement.Fit([new("DISPLAY9", 0, 0, 900, 700)], Screens, Left));
    }

    [Fact]
    public void Fit_OffTheEdge_IsPulledBackOnScreen()
    {
        SavedWindow[] saved = [new(Right, 3000, 900, 800, 600)];

        Assert.Equal(new Rectangle(2400, 384, 800, 600), WindowPlacement.Fit(saved, Screens, Right));
    }

    [Fact]
    public void Clamp_TooBig_ShrinksToTheWorkingArea()
    {
        Assert.Equal(
            new Rectangle(1920, 0, 1280, 984),
            WindowPlacement.Clamp(new Rectangle(1900, -50, 3000, 2000), Screens[1].WorkingArea));
    }

    [Fact]
    public void Remember_SameMonitor_ReplacesItsPlaceAndGoesFirst()
    {
        SavedWindow[] saved = [new(Left, 1, 1, 900, 700), new(Right, 2000, 0, 800, 600)];

        var next = WindowPlacement.Remember(saved, new(Right, 2100, 10, 800, 600));

        Assert.Equal([new(Right, 2100, 10, 800, 600), new(Left, 1, 1, 900, 700)], next);
    }

    [Fact]
    public void Remember_ManyMonitors_KeepsTheNewest()
    {
        IReadOnlyList<SavedWindow> saved = [];
        for (var i = 0; i < WindowPlacement.MaxSaved + 3; i++)
        {
            saved = WindowPlacement.Remember(saved, new($"DISPLAY{i}", 0, 0, 900, 700));
        }

        Assert.Equal(WindowPlacement.MaxSaved, saved.Count);
        Assert.Equal($"DISPLAY{WindowPlacement.MaxSaved + 2}", saved[0].Monitor);
    }

    [Fact]
    public void ReadWindows_InvalidEntries_AreDroppedAndRepaired()
    {
        var root = JsonNode.Parse(
            """{ "windowPlacements": [ { "monitor": "DISPLAY1", "x": 0, "y": 0, "width": 900, "height": 700 },""" +
            """ { "monitor": "DISPLAY2", "x": 0, "y": 0, "width": 5, "height": 700 }, "junk" ] }""")!.AsObject();
        var repaired = false;

        var places = SettingsFields.ReadWindows(root, "windowPlacements", ref repaired);

        Assert.Equal([new SavedWindow("DISPLAY1", 0, 0, 900, 700)], places);
        Assert.True(repaired);
    }
}
