namespace Naad.Settings;

/// <summary>What the settings window asks to save.</summary>
/// <param name="Settings">The new settings.</param>
/// <param name="NewKey">A new transcription API key, or null to keep the stored one.</param>
/// <param name="NewLanguageModelKey">A new language model API key, or null to keep the stored one.</param>
/// <param name="StartAtLogin">Whether Naad starts when the user signs in.</param>
internal sealed record SettingsChange(
    AppSettings Settings, string? NewKey, string? NewLanguageModelKey, bool StartAtLogin)
{
    // The generated ToString would print the keys.
    public override string ToString() =>
        $"SettingsChange {{ NewKey = {NewKey is not null}, NewLanguageModelKey = {NewLanguageModelKey is not null} }}";
}
