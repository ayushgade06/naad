using Naad.Settings;

namespace Naad.Tests.Settings;

public sealed class GeneralPanelTests
{
    [Fact]
    public void IgnoredApps_Typed_AreNamesTheDetectorCompares()
    {
        Assert.Equal(["chrome", "slack"], GeneralPanel.IgnoredApps("Chrome.exe, slack; chrome"));
    }
}
