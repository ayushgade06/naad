using System.Windows.Forms;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class TrayIconTests
{
    // Loads the embedded resource only; no icon is shown in the notification area.
    [Fact]
    public void LoadIcon_EmbeddedResource_MatchesSystemSmallIconSize()
    {
        using var icon = TrayIcon.LoadIcon();

        Assert.Equal(SystemInformation.SmallIconSize, icon.Size);
    }
}
