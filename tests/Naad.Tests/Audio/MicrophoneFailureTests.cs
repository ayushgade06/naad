using Naad.Audio;
using NAudio;

namespace Naad.Tests.Audio;

public sealed class MicrophoneFailureTests
{
    // Rows name the enum members because a public test method can't take internal enums.
    [Theory]
    [InlineData(MmResult.BadDeviceId, "NotFound")]
    [InlineData(MmResult.NoDriver, "NotFound")]
    [InlineData(MmResult.AlreadyAllocated, "InUse")]
    [InlineData(MmResult.HandleBusy, "InUse")]
    [InlineData(MmResult.UnspecifiedError, "Failed")]
    [InlineData(MmResult.WaveBadFormat, "Failed")]
    [InlineData(MmResult.MemoryAllocationError, "Failed")]
    public void FromResult_WinMmResult_MapsToError(MmResult result, string expected)
    {
        var failure = MicrophoneFailure.FromResult(result);

        Assert.Equal(Enum.Parse<MicrophoneError>(expected), failure.Error);
        Assert.Equal((int)result, failure.Code);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("Allow", "Allow", "Allow", false)]
    [InlineData("Allow", "Allow", null, false)]
    [InlineData("Prompt", "Allow", "Allow", false)]
    [InlineData("Deny", "Allow", "Allow", true)]
    [InlineData("Allow", "Deny", "Allow", true)]
    [InlineData("Allow", "Allow", "Deny", true)]
    [InlineData(null, null, "deny", true)]
    public void IsDeniedByConsent_AnyDeny_Blocks(string? device, string? user, string? desktopApps, bool expected)
    {
        Assert.Equal(expected, MicrophoneFailure.IsDeniedByConsent([device, user, desktopApps]));
    }
}
