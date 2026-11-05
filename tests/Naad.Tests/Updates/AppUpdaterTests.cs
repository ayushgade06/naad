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
    public void PublicKeys_Each_IsAP256Key()
    {
        foreach (var pem in AppUpdater.PublicKeys)
        {
            using var key = System.Security.Cryptography.ECDsa.Create();
            key.ImportFromPem(pem);

            Assert.Equal(256, key.KeySize);
        }
    }

    [Fact]
    public async Task CheckAsync_NotInstalled_FindsNothingAndNeverThrows()
    {
        Assert.Equal(new UpdateCheck(false, null), await new AppUpdater().CheckAsync());
    }
}
