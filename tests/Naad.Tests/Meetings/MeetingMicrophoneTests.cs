using Naad.Audio;
using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MeetingMicrophoneTests
{
    private static readonly string[] Endpoints =
    [
        "Microphone Array (Synthetic Audio Device)",
        "Headset Microphone (Synthetic USB Headset)",
    ];

    [Theory]
    [InlineData("Headset Microphone (Synthetic U", 1)]
    [InlineData("Microphone Array (Synthetic Aud", 0)]
    [InlineData("Unplugged synthetic microphone", -1)]
    [InlineData(null, -1)]
    [InlineData("", -1)]
    public void Find_WinMmName_MatchesTheEndpointItStarts(string? chosen, int expected)
    {
        Assert.Equal(expected, MeetingMicrophone.Find(Endpoints, chosen));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070005), nameof(MicrophoneError.Blocked))]
    [InlineData(unchecked((int)0x88890004), nameof(MicrophoneError.NotFound))]
    [InlineData(unchecked((int)0x8889000A), nameof(MicrophoneError.InUse))]
    [InlineData(unchecked((int)0x80070490), nameof(MicrophoneError.NotFound))]
    [InlineData(unchecked((int)0x80004005), nameof(MicrophoneError.Failed))]
    public void FromHResult_StartFailure_SaysWhy(int hresult, string expected)
    {
        Assert.Equal(Enum.Parse<MicrophoneError>(expected), MeetingMicrophone.FromHResult(hresult).Error);
    }
}
