using Naad.Context;
using Naad.Snippets;

namespace Naad.Tests.Snippets;

public sealed class TranscriptCommandsTests
{
    private static readonly Snippet[] Snippets =
    [
        new("my address", "1 Example Street, Testville"),
        new("my email", "someone@example.test"),
        new("my work email", "work@example.test"),
        new("sig", "Thanks, $name"),
    ];

    [Theory]
    [InlineData("Send it to my address.", "Send it to 1 Example Street, Testville")]
    [InlineData("My Address", "1 Example Street, Testville")]
    [InlineData("Mail my work email please", "Mail work@example.test please")]
    [InlineData("Mail my email", "Mail someone@example.test")]
    [InlineData("my, address", "1 Example Street, Testville")]
    [InlineData("addressing my addresses", "addressing my addresses")]
    [InlineData("sig", "Thanks, $name")]
    [InlineData("nothing here", "nothing here")]
    public void Expand_Triggers_MatchWholePhrasesLongestFirst(string text, string expected)
    {
        Assert.Equal(expected, TranscriptCommands.Expand(text, Snippets));
    }

    [Theory]
    [InlineData("Send it now. Press enter.", "Send it now.", true)]
    [InlineData("send it press Enter", "send it", true)]
    [InlineData("Press enter to continue.", "Press enter to continue.", false)]
    [InlineData("Hello.", "Hello.", false)]
    public void PressEnter_TrailingCommand_IsTakenOff(string text, string expected, bool press)
    {
        Assert.Equal((expected, press), TranscriptCommands.PressEnter(text));
    }

    [Theory]
    [InlineData("Off", false, "Dictate")]
    [InlineData("Off", true, "Dictate")]
    [InlineData("Automatic", false, "EditIfSelected")]
    [InlineData("Manual", false, "Dictate")]
    [InlineData("Manual", true, "EditRequired")]
    public void Decide_ModeAndShift_ChoosesTheIntent(string mode, bool shift, string expected)
    {
        Assert.Equal(expected, EditModeDecision.Decide(Enum.Parse<EditMode>(mode), shift).ToString());
    }

    [Fact]
    public void Expand_SnippetText_IsNotExpandedAgain()
    {
        Snippet[] snippets = [new("my sig", "Regards, sig team"), new("sig", "S")];

        Assert.Equal("Regards, sig team and S", TranscriptCommands.Expand("my sig and sig", snippets));
    }
}
