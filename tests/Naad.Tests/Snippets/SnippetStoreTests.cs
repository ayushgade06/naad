using Naad.Snippets;

namespace Naad.Tests.Snippets;

[Collection("Log")]
public sealed class SnippetStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"naad-snippets-{Guid.NewGuid():N}.dpapi");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Save_Snippets_LoadBackAndStayEncrypted()
    {
        var store = new SnippetStore(_path);
        Snippet[] snippets = [new("my address", "1 Synthetic Lane"), new("sig", "Regards")];

        Assert.True(store.Save(snippets));

        Assert.Equal(snippets, new SnippetStore(_path).Load());
        Assert.DoesNotContain("Synthetic", File.ReadAllText(_path, System.Text.Encoding.Latin1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingOrDamaged_IsEmpty()
    {
        Assert.Empty(new SnippetStore(_path).Load());
        File.WriteAllBytes(_path, [1, 2, 3]);
        Assert.Empty(new SnippetStore(_path).Load());
    }
}
