namespace Naad.Updates;

/// <summary>
/// What's new since this copy. Each release carries the changelog from its own version down
/// (the release workflow packs it as the release notes), so the sections newer than the installed
/// version cover every release skipped too. Pure; the text is the project's, not the user's.
/// </summary>
internal static class ReleaseNotes
{
    // CHANGELOG.md's version headings, as Keep a Changelog writes them: "## [2.0.1] - 2026-11-20".
    private const string Heading = "## [";

    /// <summary>
    /// The changelog sections of versions newer than <paramref name="installed"/>, newest first
    /// as the changelog lists them; the whole text when it has no version headings; empty for none.
    /// </summary>
    public static string Since(string? changelog, Version installed)
    {
        var text = (changelog ?? "").ReplaceLineEndings("\n").Trim();
        var sections = text.Split("\n" + Heading).Select((s, i) => i == 0 ? s : Heading + s).ToList();
        var versioned = sections.Where(s => Version(s) is not null).ToList();
        if (versioned.Count == 0)
        {
            return text;
        }

        return string.Join("\n\n", versioned.Where(s => Version(s) > installed).Select(s => s.Trim()));
    }

    // The version in a section's "## [x.y.z]" heading, or null for any other text.
    private static Version? Version(string section)
    {
        if (!section.StartsWith(Heading, StringComparison.Ordinal))
        {
            return null;
        }

        var end = section.IndexOf(']', StringComparison.Ordinal);
        return end > Heading.Length && System.Version.TryParse(section[Heading.Length..end], out var version)
            ? version
            : null;
    }
}
