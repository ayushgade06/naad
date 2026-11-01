using Naad.Meetings;

namespace Naad.Tests.Meetings;

public sealed class MeetingTextTests
{
    [Fact]
    public void Turns_JoinsRunsOfOneSpeakerInTimeOrderAndDropsBlanks()
    {
        MeetingSegment[] segments =
        [
            new(Speaker.Others, 5, "Synthetic reply."),
            new(Speaker.You, 0, "Synthetic one."),
            new(Speaker.You, 2, " "),
            new(Speaker.You, 3, "Synthetic two."),
            new(Speaker.You, 9, "Synthetic three."),
        ];

        var turns = MeetingTranscript.Turns(segments);

        Assert.Equal(
            [
                new(Speaker.You, 0, "Synthetic one. Synthetic two."),
                new(Speaker.Others, 5, "Synthetic reply."),
                new MeetingSegment(Speaker.You, 9, "Synthetic three."),
            ],
            turns);
    }

    [Fact]
    public void ClipboardText_NamesEachTurnsSpeaker()
    {
        var text = MeetingTranscript.ClipboardText([new(Speaker.You, 0, "Hi."), new(Speaker.Others, 1, "Hello.")]);

        Assert.Equal("You: Hi." + Environment.NewLine + "Others: Hello.", text);
    }

    [Fact]
    public void DefaultTitle_NamesTheDayAndTime() =>
        Assert.Equal("Meeting · Oct 21, 3:04pm", MeetingRecord.DefaultTitle(new DateTime(2026, 10, 21, 15, 4, 0)));
}
