using System.Security.Cryptography;
using System.Text.Json;

namespace Naad.Snippets;

/// <summary>
/// The snippets on disk: one DPAPI-encrypted file for the current Windows user, since the saved
/// text can be personal (an address, a signature). Never logged. Thread-safe.
/// </summary>
/// <param name="path">The file; a missing one means no snippets.</param>
internal sealed class SnippetStore(string path)
{
    // Ties the ciphertext to Naad's snippets. Not a secret; changing it orphans saved snippets.
    private static readonly byte[] Entropy = "Naad snippets v1"u8.ToArray();

    private readonly Lock _gate = new();

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "snippets.dpapi");

    /// <summary>The saved snippets; none when the file is missing or can't be read.</summary>
    public IReadOnlyList<Snippet> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return [];
                }

                var json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<List<Snippet>>(json) ?? [];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException
                or JsonException)
            {
                Log.Warn(LogEvent.SnippetsUnreadable);
                return [];
            }
        }
    }

    /// <summary>Replaces the saved snippets; false if the disk refused, leaving the old file.</summary>
    public bool Save(IReadOnlyList<Snippet> snippets)
    {
        lock (_gate)
        {
            var temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(temp, ProtectedData.Protect(
                    JsonSerializer.SerializeToUtf8Bytes(snippets), Entropy, DataProtectionScope.CurrentUser));
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException)
            {
                Log.Error(LogEvent.SnippetsWriteFailed, e);
                return false;
            }
        }
    }
}
