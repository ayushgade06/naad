using Naad.Transcription;

namespace Naad.Tests.Transcription;

public sealed class FillerFilterTests
{
    [Theory]
    [InlineData("you")]
    [InlineData("You.")]
    [InlineData("Thanks for watching!")]
    [InlineData("  thanks   for watching  ")]
    [InlineData("Thank you for watching.")]
    [InlineData("Thank you so much for watching!")]
    [InlineData("Please subscribe.")]
    [InlineData("Like and subscribe!")]
    [InlineData("Subtitles by the Amara.org community")]
    [InlineData("Transcription by CastingWords")]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData(" [Music] ")]
    [InlineData("(silence)")]
    public void IsFiller_WholeTranscriptIsAKnownFiller_ReturnsTrue(string transcript) =>
        Assert.True(FillerFilter.IsFiller(transcript));

    [Theory]
    [InlineData("")]
    [InlineData("Thank you.")]
    [InlineData("Thanks.")]
    [InlineData("Yes.")]
    [InlineData("Bye.")]
    [InlineData("You too.")]
    [InlineData("Are you there?")]
    [InlineData("Thanks for watching the kids last night.")]
    [InlineData("Hello world. Thanks for watching!")]
    [InlineData("Please subscribe me to the newsletter.")]
    [InlineData("ありがとう")]
    [InlineData("[note] call the bank")]
    [InlineData("Call me (later)")]
    [InlineData("[a] and [b]")]
    public void IsFiller_RealSpeechOrMixedText_ReturnsFalse(string transcript) =>
        Assert.False(FillerFilter.IsFiller(transcript));

    // whisper.cpp, which transcribes on this PC, writes these as segments of their own; the
    // installed 1.0.0 build pasted "[BLANK_AUDIO]" for a quiet recording without speech.
    [Theory]
    [InlineData(" [BLANK_AUDIO]", true)]
    [InlineData("[ Silence ]", true)]
    [InlineData(" (wind blowing)", true)]
    [InlineData(" Hello world.", false)]
    [InlineData(" [note] call the bank", false)]
    [InlineData(" Call me (later).", false)]
    [InlineData("[]x", false)]
    [InlineData("", false)]
    public void IsNonSpeechTag_ReturnsWhetherTheSegmentIsOnlyATag(string segment, bool expected) =>
        Assert.Equal(expected, FillerFilter.IsNonSpeechTag(segment));
}
