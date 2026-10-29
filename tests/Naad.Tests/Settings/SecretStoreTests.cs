using System.Security.Cryptography;
using System.Text;
using Naad.Settings;

namespace Naad.Tests.Settings;

// These use real DPAPI with this Windows user's keys: Protect and Unprotect are pure crypto
// calls and change nothing in the profile. Only files under a temp folder are written.
[Collection("Log")]
public sealed class SecretStoreTests : IDisposable
{
    // Deliberately not shaped like a real provider key, so secret scanners stay quiet.
    private const string FakeKey = "fake-api-key-0123456789";
    private const string OtherFakeKey = "fake-api-key-9876543210";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "naad-secret-tests", Guid.NewGuid().ToString("N"));

    private string KeyPath => Path.Combine(_directory, "api-key.dpapi");

    public void Dispose()
    {
        // One test logs into this directory; later tests must not log into it once it is deleted.
        Log.StopFileLogging();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Resolve_AfterSave_ReturnsStoredKey()
    {
        var store = new SecretStore(KeyPath);

        Assert.Null(store.Save(FakeKey));
        var result = store.Resolve(overrideValue: null);

        Assert.Equal(new ApiKeyResult(FakeKey, ApiKeySource.Stored, SecretStatus.Loaded), result);
    }

    [Fact]
    public void Save_Twice_KeepsOnlyTheNewKey()
    {
        var store = new SecretStore(KeyPath);

        store.Save(FakeKey);
        store.Save(OtherFakeKey);

        Assert.Equal(OtherFakeKey, store.Resolve(null).Key);
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Save_KeyWithSurroundingWhitespace_StoresItTrimmed()
    {
        var store = new SecretStore(KeyPath);

        store.Save($"  {FakeKey}\r\n");

        Assert.Equal(FakeKey, store.Resolve(null).Key);
    }

    [Fact]
    public void Save_File_HoldsNoPlaintext()
    {
        new SecretStore(KeyPath).Save(FakeKey);

        var bytes = File.ReadAllBytes(KeyPath);

        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(FakeKey)));
        Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(FakeKey)));
        Assert.False(Contains(bytes, Encoding.UTF8.GetBytes("0123456789")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Save_BlankKey_Throws(string key) =>
        Assert.Throws<ArgumentException>(() => new SecretStore(KeyPath).Save(key));

    // PowerShell 5.1 pipes text to a native program as UTF-8 with a byte-order mark. Read with
    // the console's code page, the mark became three stray characters at the start of the key,
    // and every request then failed because the header wasn't ASCII.
    [Fact]
    public void ReadKey_PowerShellPipeWithUtf8Mark_ReturnsJustTheKey()
    {
        byte[] piped = [0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(FakeKey + "\r\n")];

        Assert.Equal(FakeKey, SecretStore.ReadKey(new MemoryStream(piped)));
    }

    [Fact]
    public void ReadKey_Utf16WithMark_ReturnsJustTheKey()
    {
        byte[] piped = [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(FakeKey + "\n")];

        Assert.Equal(FakeKey, SecretStore.ReadKey(new MemoryStream(piped)));
    }

    [Fact]
    public void ReadKey_PlainAscii_ReturnsTheKeyTrimmed() =>
        Assert.Equal(FakeKey, SecretStore.ReadKey(new MemoryStream(Encoding.ASCII.GetBytes($"  {FakeKey}\n"))));

    [Theory]
    [InlineData("fake-api-key-0123456789", true)]
    [InlineData("A1_b2-C3.d4", true)]
    [InlineData("", false)]
    [InlineData("has space", false)]
    [InlineData("ï»¿fake-key", false)]
    [InlineData("﻿fake-key", false)]
    [InlineData("fake key", false)]
    [InlineData("fake\tkey", false)]
    [InlineData("café", false)]
    public void IsValidKey_OnlyVisibleAscii_IsValid(string key, bool expected) =>
        Assert.Equal(expected, SecretStore.IsValidKey(key));

    [Fact]
    public void Save_KeyWithStrayCharacters_ThrowsAndWritesNothing()
    {
        Assert.Throws<ArgumentException>(() => new SecretStore(KeyPath).Save("ï»¿" + FakeKey));

        Assert.False(File.Exists(KeyPath));
    }

    [Fact]
    public void Resolve_OverrideWithStrayCharacters_IsInvalidAndNotUsed()
    {
        var store = new SecretStore(KeyPath);
        store.Save(FakeKey);

        var result = store.Resolve("﻿" + OtherFakeKey);

        Assert.Equal(new ApiKeyResult(null, ApiKeySource.None, SecretStatus.Invalid), result);
    }

    [Fact]
    public void Resolve_StoredKeyWithStrayCharacters_IsUnreadable()
    {
        // A key saved by --set-api-key before stdin was read as UTF-8.
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(
            KeyPath,
            ProtectedData.Protect(
                Encoding.UTF8.GetBytes("ï»¿" + FakeKey),
                "Naad API key v1"u8.ToArray(),
                DataProtectionScope.CurrentUser));

        var result = new SecretStore(KeyPath).Resolve(null);

        Assert.Equal(SecretStatus.Unreadable, result.Status);
        Assert.Null(result.Key);
    }

    [Fact]
    public void Resolve_NoFile_IsMissing()
    {
        var result = new SecretStore(KeyPath).Resolve(null);

        Assert.Equal(new ApiKeyResult(null, ApiKeySource.None, SecretStatus.Missing), result);
    }

    [Fact]
    public void Resolve_GarbageFile_IsUnreadable()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(KeyPath, [1, 2, 3, 4, 5, 6, 7, 8]);

        var result = new SecretStore(KeyPath).Resolve(null);

        Assert.Null(result.Key);
        Assert.Equal(SecretStatus.Unreadable, result.Status);
        Assert.IsAssignableFrom<CryptographicException>(result.Error);
    }

    [Fact]
    public void Resolve_EmptyFile_IsUnreadable()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(KeyPath, []);

        Assert.Equal(SecretStatus.Unreadable, new SecretStore(KeyPath).Resolve(null).Status);
    }

    [Fact]
    public void Resolve_TruncatedCiphertext_IsUnreadable()
    {
        var store = new SecretStore(KeyPath);
        store.Save(FakeKey);
        var bytes = File.ReadAllBytes(KeyPath);
        File.WriteAllBytes(KeyPath, bytes[..(bytes.Length / 2)]);

        Assert.Equal(SecretStatus.Unreadable, store.Resolve(null).Status);
    }

    [Fact]
    public void Resolve_BlobFromAnotherProgram_IsUnreadable()
    {
        // Same user and scope but no Naad entropy, as another program's DPAPI blob would be.
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(
            KeyPath,
            ProtectedData.Protect(Encoding.UTF8.GetBytes(FakeKey), null, DataProtectionScope.CurrentUser));

        var result = new SecretStore(KeyPath).Resolve(null);

        Assert.Equal(SecretStatus.Unreadable, result.Status);
        Assert.Null(result.Key);
    }

    [Fact]
    public void Resolve_FileLockedByAnotherProcess_IsReadFailed()
    {
        var store = new SecretStore(KeyPath);
        store.Save(FakeKey);
        using var lockHolder = new FileStream(KeyPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = store.Resolve(null);

        Assert.Equal(SecretStatus.ReadFailed, result.Status);
        Assert.IsAssignableFrom<IOException>(result.Error);
    }

    [Fact]
    public void Resolve_OverrideSet_WinsWithoutReadingTheStore()
    {
        // A garbage file proves the store isn't read: reading it would report Unreadable.
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(KeyPath, [1, 2, 3]);

        var result = new SecretStore(KeyPath).Resolve($" {OtherFakeKey} ");

        Assert.Equal(new ApiKeyResult(OtherFakeKey, ApiKeySource.Environment, SecretStatus.Loaded), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolve_BlankOverride_UsesStoredKey(string? overrideValue)
    {
        var store = new SecretStore(KeyPath);
        store.Save(FakeKey);

        var result = store.Resolve(overrideValue);

        Assert.Equal(ApiKeySource.Stored, result.Source);
        Assert.Equal(FakeKey, result.Key);
    }

    [Fact]
    public void Save_FolderCantBeCreated_ReturnsErrorAndWritesNothing()
    {
        // A file where the key's folder should be.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "");

        var error = new SecretStore(Path.Combine(blocker, "api-key.dpapi")).Save(FakeKey);

        var io = Assert.IsAssignableFrom<IOException>(error);
        Assert.DoesNotContain(FakeKey, io.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_TargetLocked_ReturnsErrorAndKeepsPreviousKey()
    {
        var store = new SecretStore(KeyPath);
        store.Save(FakeKey);

        Exception? error;
        using (new FileStream(KeyPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            error = store.Save(OtherFakeKey);
        }

        Assert.NotNull(error);
        Assert.Equal(FakeKey, store.Resolve(null).Key);
        Assert.False(File.Exists(KeyPath + ".tmp"));
    }

    [Fact]
    public void ApiKeyResult_ToString_OmitsTheKey()
    {
        var text = new ApiKeyResult(FakeKey, ApiKeySource.Stored, SecretStatus.Loaded).ToString();

        Assert.DoesNotContain(FakeKey, text, StringComparison.Ordinal);
        Assert.Contains("Stored", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LogOutcome_EachResult_WritesFixedEventAndNeverTheKey()
    {
        (ApiKeyResult Result, string Expected)[] outcomes =
        [
            (new(FakeKey, ApiKeySource.Environment, SecretStatus.Loaded), "INFO ApiKeyFromEnvironment"),
            (new(FakeKey, ApiKeySource.Stored, SecretStatus.Loaded), "INFO ApiKeyLoaded"),
            (new(null, ApiKeySource.None, SecretStatus.Missing), "ERROR ApiKeyMissing"),
            (new(null, ApiKeySource.None, SecretStatus.Invalid), "ERROR ApiKeyInvalid"),
            (new(null, ApiKeySource.None, SecretStatus.Unreadable, new CryptographicException(FakeKey)),
                "ERROR ApiKeyUnreadable exception=System.Security.Cryptography.CryptographicException"),
            (new(null, ApiKeySource.None, SecretStatus.ReadFailed, new IOException(FakeKey)),
                "ERROR ApiKeyUnreadable exception=System.IO.IOException"),
            (new(null, ApiKeySource.None, SecretStatus.Unreadable), "ERROR ApiKeyUnreadable"),
        ];
        Log.Configure(_directory);

        foreach (var (result, _) in outcomes)
        {
            SecretStore.LogOutcome(result);
        }

        var log = File.ReadAllText(Path.Combine(_directory, Log.FileName));
        foreach (var (_, expected) in outcomes)
        {
            Assert.Contains(expected, log, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(FakeKey, log, StringComparison.Ordinal);
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
