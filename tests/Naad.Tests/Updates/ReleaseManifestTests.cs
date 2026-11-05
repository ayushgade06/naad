using System.Security.Cryptography;
using System.Text;
using Naad.Updates;

namespace Naad.Tests.Updates;

public sealed class ReleaseManifestTests
{
    private const string Package = "Naad.App-9.9.9-full.nupkg";

    // A synthetic package and a throwaway key: never Naad's own.
    private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("synthetic package bytes");
    private static readonly string PackageHash = Convert.ToHexString(SHA256.HashData(PackageBytes)).ToLowerInvariant();

    private static readonly string Json =
        "{\"packId\":\"Naad.App\",\"version\":\"9.9.9\",\"published\":\"2026-11-01T09:00:00+00:00\"," +
        $"\"files\":[{{\"name\":\"{Package}\",\"size\":{PackageBytes.Length},\"sha256\":\"{PackageHash}\"}}]}}";

    [Fact]
    public void Verify_SignedByTheKey_ReadsTheList()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(key, Json);

        var manifest = ReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem());

        Assert.NotNull(manifest);
        Assert.Equal(
            ("9.9.9", new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero)), (manifest.Version, manifest.Published));
        Assert.True(manifest.Covers("Naad.App", "9.9.9", Package, PackageBytes.Length, PackageHash.ToUpperInvariant()));
    }

    [Fact]
    public void Verify_AsTheReleaseWorkflowWritesIt_ReadsEveryFile()
    {
        // ConvertTo-Json -Compress of the workflow's ordered table: 7-digit fractions, lowercase hashes.
        var json = "{\"packId\":\"Naad.App\",\"version\":\"9.9.9\"," +
            "\"published\":\"2026-11-05T12:00:00.1234567+00:00\",\"files\":[" +
            "{\"name\":\"Naad.App-9.9.9-full.nupkg\",\"size\":61234567,\"sha256\":\"" + new string('a', 64) + "\"}," +
            "{\"name\":\"Naad.App-win-Setup.exe\",\"size\":1024,\"sha256\":\"" + new string('b', 64) + "\"}," +
            "{\"name\":\"releases.win.json\",\"size\":512,\"sha256\":\"" + new string('c', 64) + "\"}]}";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(key, json);

        var manifest = ReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem());

        Assert.NotNull(manifest);
        Assert.Equal(3, manifest.Files.Count);
        Assert.True(manifest.Covers(
            "Naad.App", "9.9.9", "Naad.App-9.9.9-full.nupkg", 61234567, new string('A', 64)));
    }

    [Fact]
    public void Verify_NoPublishTime_IsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(key, Json.Replace("\"published\":\"2026-11-01T09:00:00+00:00\",", ""));

        Assert.Null(ReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Verify_OneByteChanged_IsRefused()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(key, Json);
        bytes[^3] ^= 1;

        Assert.Null(ReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Verify_SignedByAnotherKey_IsRefused()
    {
        using var publisher = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(stranger, Json);

        Assert.Null(ReleaseManifest.Verify(bytes, signature, publisher.ExportSubjectPublicKeyInfoPem()));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("")]
    public void Verify_BrokenSignature_IsRefused(string signature)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Null(
            ReleaseManifest.Verify(Encoding.UTF8.GetBytes(Json), signature, key.ExportSubjectPublicKeyInfoPem()));
    }

    [Fact]
    public void Covers_OtherVersionSizeOrHash_IsFalse()
    {
        var manifest = Manifest();

        Assert.False(manifest.Covers("Naad.App", "9.9.8", Package, PackageBytes.Length, PackageHash));
        Assert.False(manifest.Covers("Naad.App", "9.9.9", Package, PackageBytes.Length + 1, PackageHash));
        Assert.False(manifest.Covers("Naad.App", "9.9.9", Package, PackageBytes.Length, new string('0', 64)));
        Assert.False(manifest.Covers("Other.App", "9.9.9", Package, PackageBytes.Length, PackageHash));
    }

    [Fact]
    public void Matches_TamperedPackage_IsFalse()
    {
        var manifest = Manifest();
        var tampered = (byte[])PackageBytes.Clone();
        tampered[0] ^= 1;

        Assert.True(manifest.Matches(Package, new MemoryStream(PackageBytes)));
        Assert.False(manifest.Matches(Package, new MemoryStream(tampered)));
        Assert.False(manifest.Matches(Package, new MemoryStream([.. PackageBytes, 0])));
        Assert.False(manifest.Matches("Naad.App-9.9.9-delta.nupkg", new MemoryStream(PackageBytes)));
    }

    private static ReleaseManifest Manifest()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (bytes, signature) = Sign(key, Json);
        return ReleaseManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem())!;
    }

    private static (byte[] Bytes, string Signature) Sign(ECDsa key, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return (bytes, Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256)));
    }
}
