using System.Security.Cryptography;
using System.Text.Json;

namespace Naad.Updates;

/// <summary>One file of a release, as the release list names it.</summary>
/// <param name="Name">The file name, such as "Naad.App-2.2.0-full.nupkg".</param>
/// <param name="Size">Its length in bytes.</param>
/// <param name="Sha256">Its SHA-256, in hex.</param>
internal sealed record ReleaseFile(string Name, long Size, string Sha256);

/// <summary>
/// The signed release list published with every release: its version, when it was published, and
/// each file's size and hash. Signed in the release workflow with Naad's private key (ECDSA P-256,
/// SHA-256); the public key is built into Naad, so only a release made with the same key installs.
/// Pure.
/// </summary>
/// <param name="PackId">The package's id, "Naad.App".</param>
/// <param name="Version">The release's version, such as "2.2.0".</param>
/// <param name="Published">When the release workflow signed it, in UTC.</param>
/// <param name="Files">The release's files.</param>
internal sealed record ReleaseManifest(
    string PackId, string Version, DateTimeOffset Published, IReadOnlyList<ReleaseFile> Files)
{
    /// <summary>The release asset holding the list.</summary>
    public const string FileName = "naad-release.json";

    /// <summary>The release asset holding its signature: base64 of the 64-byte r‖s value.</summary>
    public const string SignatureFileName = FileName + ".sig";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The list in <paramref name="json"/> when <paramref name="signature"/> is a valid signature of
    /// exactly those bytes by <paramref name="publicKeyPem"/>; null for anything else. The bytes are
    /// checked before they are read, so nothing unsigned is ever trusted.
    /// </summary>
    public static ReleaseManifest? Verify(byte[] json, string signature, string publicKeyPem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (!key.VerifyData(json, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256))
            {
                return null;
            }

            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(json, Options);
            // Without a publish time the three-day wait couldn't be kept, so such a list is refused.
            return manifest is { PackId: not null, Version: not null, Files.Count: > 0 }
                && manifest.Published != default ? manifest : null;
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException
            or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the list vouches for <paramref name="fileName"/> of <paramref name="packId"/>
    /// <paramref name="version"/> with that size and SHA-256, as the update feed describes it.
    /// </summary>
    public bool Covers(string packId, string version, string fileName, long size, string? sha256) =>
        PackId == packId && Version == version
        && Files.Any(f => f.Name == fileName && f.Size == size
            && string.Equals(f.Sha256, sha256, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="content"/> is the file the list names <paramref name="fileName"/>.</summary>
    public bool Matches(string fileName, Stream content)
    {
        if (Files.FirstOrDefault(f => f.Name == fileName) is not { } file || content.Length != file.Size)
        {
            return false;
        }

        return string.Equals(
            Convert.ToHexString(SHA256.HashData(content)), file.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
