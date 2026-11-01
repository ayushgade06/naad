using Naad.Settings;

namespace Naad.Tests.Settings;

public sealed class SettingsStoreLiveTests
{
    [Fact]
    public void Parse_LiveSettings_ReturnsThem()
    {
        var result = SettingsStore.Parse("""{ "version": 4, "livePreview": true }""");

        Assert.Equal(SettingsStatus.Loaded, result.Status);
        Assert.True(result.Settings.LivePreview);
    }

    [Fact]
    public void Parse_LivePreviewNotABool_RepairsToOff()
    {
        var result = SettingsStore.Parse("""{ "version": 4, "livePreview": "yes" }""");

        Assert.Equal(SettingsStatus.Repaired, result.Status);
        Assert.False(result.Settings.LivePreview);
    }
}
