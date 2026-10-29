using Naad.Audio;

namespace Naad.Tests.Audio;

// Only the pure device choice; opening a real microphone is covered by manual checks.
public sealed class AudioRecorderTests
{
    private const int WindowsDefault = -1;
    private static readonly string[] Devices = ["Microphone (Test Device)", "Headset (Test Device)"];

    [Theory]
    [InlineData("Microphone (Test Device)", 0)]
    [InlineData("Headset (Test Device)", 1)]
    public void DeviceNumber_ConnectedDevice_ReturnsItsIndex(string microphone, int expected)
    {
        Assert.Equal(expected, AudioRecorder.DeviceNumber(Devices, microphone));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Unplugged (Test Device)")]
    [InlineData("headset (test device)")]
    public void DeviceNumber_NoChoiceOrNotConnected_ReturnsWindowsDefault(string? microphone)
    {
        Assert.Equal(WindowsDefault, AudioRecorder.DeviceNumber(Devices, microphone));
    }

    [Fact]
    public void DeviceNumber_SameNameTwice_ReturnsTheFirst()
    {
        Assert.Equal(0, AudioRecorder.DeviceNumber(["USB Mic", "USB Mic"], "USB Mic"));
    }

    [Fact]
    public void DeviceNumber_NoDevices_ReturnsWindowsDefault()
    {
        Assert.Equal(WindowsDefault, AudioRecorder.DeviceNumber([], "USB Mic"));
    }
}
