using System.Reflection;
using System.Runtime.InteropServices;
using Naad.Settings;

namespace Naad.Window;

/// <summary>
/// The build and setup details a bug report needs, for the Diagnostics page's Copy. Names of
/// choices only: no keys, no device names, nothing the user said or typed. Pure apart from
/// reading the build and the Windows version.
/// </summary>
internal static class Diagnostics
{
    // Git's default short hash: enough to find the commit.
    private const int ShortCommitLength = 7;

    /// <summary>Lines such as "Naad 2.0.0 (abc1234)" and "Windows 10.0.26100 (X64)".</summary>
    public static string Describe(AppSettings s) => string.Join(Environment.NewLine,
    [
        $"Naad {Build()}",
        $"Windows {Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})",
        $".NET {Environment.Version}",
        $"Transcription: {Transcription(s)}",
        $"Language model: {(s.LanguageModelProvider == LanguageModel.LanguageModelProvider.Local
            ? $"this PC, {s.LocalLanguageModel}" : $"cloud, {s.LanguageModelService}")}; cleanup {s.Cleanup}",
        $"Live: preview {OnOff(s.LivePreview)}, typing {OnOff(s.LiveTyping)}, on {s.LiveService}",
        $"Context {OnOff(s.Context)}, Edit Mode {s.EditMode} ({s.EditModeKey})",
        $"Shortcuts: hold {OnOff(s.HoldShortcut is not null)}, toggle {OnOff(s.ToggleShortcut is not null)}, " +
            $"paste again {OnOff(s.PasteAgainShortcut is not null)}, start delay {s.StartDelayMilliseconds} ms",
        $"History {s.HistoryRetention}, meeting offers {s.MeetingDetection}",
        $"Settings version {s.Version}",
    ]);

    // The version a release is stamped with, and the commit when the build recorded one.
    private static string Build()
    {
        var version = typeof(Diagnostics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0 || plus == version.Length - 1)
        {
            return plus < 0 ? version : version[..plus];
        }

        var commit = version[(plus + 1)..];
        return $"{version[..plus]} ({commit[..Math.Min(ShortCommitLength, commit.Length)]})";
    }

    private static string Transcription(AppSettings s) =>
        s.TranscriptionProvider == Naad.Transcription.TranscriptionProvider.Local
            ? $"this PC, Whisper {s.LocalModel}"
            : $"cloud, {s.TranscriptionService}";

    private static string OnOff(bool on) => on ? "on" : "off";
}
