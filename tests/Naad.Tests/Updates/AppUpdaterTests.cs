using Naad.Updates;
using Velopack;

namespace Naad.Tests.Updates;

// The test runner isn't an installed copy of Naad, like every developer and CI build: the
// updater must then do nothing at all. Updating an installed copy is covered by M-P5 and M-P6.
[Collection("Log")]
public sealed class AppUpdaterTests
{
    // Main runs this before anything else. With no arguments it runs no install hook; it only
    // tells Velopack where the app lives, which UpdateManager needs.
    static AppUpdaterTests() => VelopackApp.Build().SetArgs([]).Run();

    [Fact]
    public void IsInstalled_BuildFromSource_IsFalse()
    {
        Assert.False(new AppUpdater().IsInstalled);
    }

    [Fact]
    public async Task CheckAsync_NotInstalled_FindsNothingAndNeverThrows()
    {
        Assert.Equal((false, null), await new AppUpdater().CheckAsync());
    }

    [Fact]
    public async Task DownloadAndApplyOnExitAsync_NothingFound_DoesNothing()
    {
        Assert.False(await new AppUpdater().DownloadAsync(
            cancellationToken: TestContext.Current.CancellationToken));
    }
}
