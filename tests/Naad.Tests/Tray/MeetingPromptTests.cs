using System.Drawing;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class MeetingPromptTests
{
    [Fact]
    public void Place_Prompt_SitsCentredAboveTheBottom()
    {
        var point = MeetingPrompt.Place(new Rectangle(0, 0, 1920, 1040), new Size(380, 132), 96);

        Assert.Equal(new Point(770, 812), point);
    }
}
