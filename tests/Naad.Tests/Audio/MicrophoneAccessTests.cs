using Naad.Audio;

namespace Naad.Tests.Audio;

public sealed class MicrophoneAccessTests
{
    [Theory]
    [InlineData(true, null, null, null, nameof(MicrophoneAccess.Allowed))]
    [InlineData(true, "Allow", "Allow", "Allow", nameof(MicrophoneAccess.Allowed))]
    [InlineData(true, "Deny", "Allow", "Allow", nameof(MicrophoneAccess.OffForDevice))]
    [InlineData(true, "Allow", "deny", "Allow", nameof(MicrophoneAccess.OffForUser))]
    [InlineData(true, null, null, "Deny", nameof(MicrophoneAccess.OffForDesktopApps))]
    [InlineData(false, null, null, null, nameof(MicrophoneAccess.Unknown))]
    public void Access_ConsentValues_SayWhichSwitchIsOff(
        bool readable, string? device, string? user, string? desktop, string expected)
    {
        Assert.Equal(Enum.Parse<MicrophoneAccess>(expected), MicrophoneFailure.Access(readable, device, user, desktop));
    }

    [Fact]
    public void Describe_EveryAccess_HasWords()
    {
        foreach (var access in Enum.GetValues<MicrophoneAccess>())
        {
            Assert.NotEqual(access.ToString(), MicrophoneFailure.Describe(access));
        }
    }
}
