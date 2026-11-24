using Naad.Meetings;

namespace Naad.Tests.Meetings;

// Synthetic lines shaped like the ones laptop speakers produced in a real test call.
public sealed class MeetingEchoTests
{
    [Fact]
    public void Without_YouLinesEchoingOthers_AreLeftOut()
    {
        MeetingSegment[] lines =
        [
            new(Speaker.You, 1.0, "Welcome to the planning call."),
            new(Speaker.Others, 1.1, "Welcome to the planning call."),
            new(Speaker.You, 6.0, "need to people by March."),
            new(Speaker.Others, 5.8, "We need two people by March."),
        ];

        Assert.Equal([lines[1], lines[3]], MeetingEcho.Without(lines));
    }

    [Fact]
    public void Without_TheUsersOwnReply_Stays()
    {
        MeetingSegment[] lines =
        [
            new(Speaker.Others, 1.0, "Can we ship the release on Monday morning?"),
            new(Speaker.You, 4.0, "Monday works, but I need the notes by Friday."),
            new(Speaker.You, 30.0, "Can we ship the release on Monday morning?"),
        ];

        // The reply shares few words; the repeat came long after, so it isn't an echo either.
        Assert.Equal(lines, MeetingEcho.Without(lines));
    }

    [Fact]
    public void Without_ShortRepliesAndFailedPieces_Stay()
    {
        MeetingSegment[] lines =
        [
            new(Speaker.Others, 1.0, "Okay, sure."),
            new(Speaker.You, 1.2, "Okay, sure."),
            new(Speaker.Others, 9.0, MeetingSegment.Failed),
            new(Speaker.You, 9.1, MeetingSegment.Failed),
        ];

        Assert.Equal(lines, MeetingEcho.Without(lines));
    }

    [Fact]
    public void Without_OnlyYou_ChangesNothing()
    {
        MeetingSegment[] lines = [new(Speaker.You, 1.0, "Notes for a meeting in the room.")];

        Assert.Same(lines, MeetingEcho.Without(lines));
    }
}
