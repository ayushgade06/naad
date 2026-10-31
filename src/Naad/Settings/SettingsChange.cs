using Naad.Providers;

namespace Naad.Settings;

/// <summary>What the settings window asks to save.</summary>
/// <param name="Settings">The new settings.</param>
/// <param name="NewKeys">New API keys by service; empty to keep the stored ones.</param>
/// <param name="StartAtLogin">Whether Naad starts when the user signs in.</param>
internal sealed record SettingsChange(
    AppSettings Settings, IReadOnlyDictionary<CloudService, string> NewKeys, bool StartAtLogin)
{
    // The generated ToString would print the keys.
    public override string ToString() => $"SettingsChange {{ NewKeys = {NewKeys.Count} }}";
}
