using Naad.Updates;

namespace Naad.Tests.Updates;

public sealed class ReleaseNotesTests
{
    private const string Changelog = """
        ## [2.0.3] - 2026-12-01

        ### Added

        - A synthetic feature.

        ## [2.0.2] - 2026-11-20

        - A faster synthetic thing.

        ## [2.0.1] - 2026-11-10

        - A synthetic fix.
        """;

    [Fact]
    public void Since_SkippedVersions_ListsEveryNewerReleaseNewestFirst()
    {
        var notes = ReleaseNotes.Since(Changelog, new Version(2, 0, 1));

        Assert.StartsWith("## [2.0.3]", notes, StringComparison.Ordinal);
        Assert.Contains("## [2.0.2]", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("2.0.1", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("A synthetic fix.", notes, StringComparison.Ordinal);
    }

    [Fact]
    public void Since_OneVersionBehind_ListsOnlyTheNewest() =>
        Assert.Equal(
            "## [2.0.3] - 2026-12-01\n\n### Added\n\n- A synthetic feature.",
            ReleaseNotes.Since(Changelog.ReplaceLineEndings("\r\n"), new Version(2, 0, 2)));

    [Fact]
    public void Since_NotesWithoutVersionHeadings_AreShownWhole() =>
        Assert.Equal(
            "- Something synthetic.", ReleaseNotes.Since("  - Something synthetic.  ", new Version(2, 0, 0)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Since_NoNotes_IsEmpty(string? notes) =>
        Assert.Equal("", ReleaseNotes.Since(notes, new Version(2, 0, 0)));
}
