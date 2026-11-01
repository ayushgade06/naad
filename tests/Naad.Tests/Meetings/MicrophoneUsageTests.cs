using Naad.Meetings;

namespace Naad.Tests.Meetings;

// Synthetic consent-store key names and FILETIMEs.
public sealed class MicrophoneUsageTests
{
    [Theory]
    [InlineData(@"C:#Users#Someone#AppData#Roaming#Zoom#bin#Zoom.exe", false, "zoom")]
    [InlineData(@"C:#Program Files#Google#Chrome#Application#chrome.exe", false, "chrome")]
    [InlineData("MSTeams_8wekyb3d8bbwe", true, "msteams")]
    [InlineData("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", true, "windowssoundrecorder")]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", true, "whatsappdesktop")]
    public void AppName_FromConsentStoreKey(string key, bool packaged, string expected) =>
        Assert.Equal(expected, MicrophoneUsage.AppName(key, packaged));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(200, 0, true)]
    [InlineData(200, 100, true)]
    [InlineData(100, 200, false)]
    public void InUse_StartLaterThanStop(long start, long stop, bool expected) =>
        Assert.Equal(expected, MicrophoneUsage.InUse(start, stop));
}
