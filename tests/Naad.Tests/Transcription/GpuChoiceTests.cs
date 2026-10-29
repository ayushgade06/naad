using Naad.Transcription;

namespace Naad.Tests.Transcription;

public sealed class GpuChoiceTests
{
    // Device kinds as ggml numbers them: 0 CPU, 1 discrete GPU, 2 integrated GPU, 3 accelerator.
    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 0 }, 0)]
    [InlineData(new[] { 2, 0 }, 0)]
    [InlineData(new[] { 2, 1, 0 }, 1)]
    [InlineData(new[] { 1, 2, 0 }, 0)]
    [InlineData(new[] { 2, 2, 1, 0 }, 2)]
    [InlineData(new[] { 1, 1, 0 }, 0)]
    [InlineData(new[] { 0, 3, 1 }, 0)]
    [InlineData(new[] { 3, 2, 1 }, 1)]
    public void Pick_Devices_ReturnsFirstDiscreteCardNumber(int[] kinds, int expected)
    {
        var devices = kinds.Select(kind => (GgmlDeviceType)kind);

        Assert.Equal(expected, GpuChoice.Pick(devices));
    }
}
