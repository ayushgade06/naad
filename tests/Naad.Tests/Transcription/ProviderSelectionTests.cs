using Naad.Transcription;

namespace Naad.Tests.Transcription;

public sealed class ProviderSelectionTests
{
    [Theory]
    [InlineData("Cloud", true, false, "Cloud")]
    [InlineData("Cloud", true, true, "Cloud")]
    [InlineData("Local", false, true, "Local")]
    [InlineData("Local", true, true, "Local")]
    public void Choose_ChosenProviderCanRun_ReturnsIt(string chosen, bool hasKey, bool modelReady, string expected)
    {
        Assert.Equal(expected, ProviderSelection.Choose(Parse(chosen), hasKey, modelReady)?.ToString());
    }

    [Theory]
    // Cloud without a key, even with the model downloaded.
    [InlineData("Cloud", false, false)]
    [InlineData("Cloud", false, true)]
    // Local without the model never falls back to uploading, even with a key.
    [InlineData("Local", true, false)]
    [InlineData("Local", false, false)]
    public void Choose_ChosenProviderCantRun_ReturnsNullNeverTheOther(string chosen, bool hasKey, bool modelReady)
    {
        Assert.Null(ProviderSelection.Choose(Parse(chosen), hasKey, modelReady));
    }

    private static TranscriptionProvider Parse(string name) => Enum.Parse<TranscriptionProvider>(name);
}
