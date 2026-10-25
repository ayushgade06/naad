using Naad.Hotkey;

namespace Naad.Settings;

/// <summary>
/// User settings kept in settings.json. Add a property only when the app reads it. Secrets
/// such as API keys, and user content such as transcripts, never belong here.
/// </summary>
internal sealed record AppSettings
{
    /// <summary>
    /// Bump when a property is renamed, removed or changes meaning, and add the matching
    /// migration to <see cref="SettingsStore"/>. Adding a property with a default needs no bump.
    /// </summary>
    public const int CurrentVersion = 1;

    public static AppSettings Defaults { get; } = new();

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Write the diagnostic log file. The log never holds user content (see Log).</summary>
    public bool FileLogging { get; init; } = true;

    /// <summary>The keys to hold while dictating, written like "RightCtrl" (see Chord).</summary>
    public Chord DictationShortcut { get; init; } = Chord.RightCtrl;
}
