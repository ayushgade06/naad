using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

public sealed class LanguageModelSelectionTests
{
    [Theory]
    [InlineData("Cloud", true, false, false, "Cloud")]
    // A server on this PC, such as Ollama, may need no key.
    [InlineData("Cloud", false, true, false, "Cloud")]
    [InlineData("Cloud", false, false, true, null)]
    [InlineData("Local", false, false, true, "Local")]
    // Local without the model never falls back to uploading, even with a key.
    [InlineData("Local", true, true, false, null)]
    public void Choose_WhatIsInPlace_NeverSwitchesProvider(
        string chosen, bool hasKey, bool loopback, bool modelReady, string? expected)
    {
        var provider = Enum.Parse<LanguageModelProvider>(chosen);

        Assert.Equal(expected, LanguageModelSelection.Choose(provider, hasKey, loopback, modelReady)?.ToString());
    }
}
