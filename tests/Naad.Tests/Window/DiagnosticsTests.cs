using Naad.Settings;
using Naad.Window;

namespace Naad.Tests.Window;

public sealed class DiagnosticsTests
{
    [Fact]
    public void Describe_NamesChoicesButNoUserContent()
    {
        var settings = AppSettings.Defaults with
        {
            Words = ["Synthetic-Name"],
            Microphone = "Synthetic Microphone",
            CleanupPrompt = "synthetic instructions",
            ContextExcludedApps = ["synthetic-app"],
        };

        var details = Diagnostics.Describe(settings);

        Assert.StartsWith("Naad ", details, StringComparison.Ordinal);
        Assert.Contains("Transcription: this PC, Whisper Base", details, StringComparison.Ordinal);
        Assert.Contains("Windows ", details, StringComparison.Ordinal);
        string[] secrets = ["Synthetic-Name", "Synthetic Microphone", "synthetic instructions", "synthetic-app"];
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, details, StringComparison.Ordinal);
        }
    }
}
