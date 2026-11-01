using Naad.Context;

namespace Naad.Tests.Context;

public sealed class EditModeDecisionTests
{
    [Theory]
    [InlineData("Off", false, "Dictate")]
    [InlineData("Off", true, "Dictate")]
    [InlineData("Automatic", false, "EditIfSelected")]
    [InlineData("Automatic", true, "EditIfSelected")]
    [InlineData("Manual", false, "Dictate")]
    [InlineData("Manual", true, "EditRequired")]
    public void Decide_ModeAndKey_ReadsTheSelectionOnlyWhenAsked(string mode, bool keyHeld, string expected) =>
        Assert.Equal(
            Enum.Parse<EditIntent>(expected), EditModeDecision.Decide(Enum.Parse<EditMode>(mode), keyHeld));
}
