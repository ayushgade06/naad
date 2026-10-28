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
    public void IsFiller_RealSpeechOrMixedText_ReturnsFalse(string transcript) =>
        Assert.False(FillerFilter.IsFiller(transcript));
}
