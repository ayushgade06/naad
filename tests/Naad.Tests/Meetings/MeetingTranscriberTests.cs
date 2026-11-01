using Naad.Meetings;
using Naad.Tests.Dictation;
using Naad.Transcription;
using static Naad.Tests.Live.SyntheticAudio;

namespace Naad.Tests.Meetings;

[Collection("Log")]
public sealed class MeetingTranscriberTests
{
    [Fact]
    public async Task FinishAsync_BothTracks_ReturnsSegmentsInTimeOrder()
    {
        var transcriber = Numbered();
        var meeting = new MeetingTranscriber(transcriber, keepAudio: null);

        meeting.Add(Speaker.You, [.. Loud(1), .. Quiet(3)], TimeSpan.FromSeconds(4));
        meeting.Add(Speaker.Others, [.. Quiet(5), .. Loud(1), .. Quiet(3)], TimeSpan.FromSeconds(9));
        var segments = await meeting.FinishAsync();

        Assert.Equal([Speaker.You, Speaker.Others], segments.Select(s => s.Speaker));
        Assert.Equal(0, segments[0].Start, 1);
        Assert.True(segments[1].Start >= 4);
    }

    [Fact]
    public async Task FinishAsync_LastPieceWithoutPause_IsTranscribed()
    {
        var meeting = new MeetingTranscriber(Numbered(), keepAudio: null);

        meeting.Add(Speaker.You, Loud(3), TimeSpan.FromSeconds(3));
        var segments = await meeting.FinishAsync();

        Assert.Equal("piece 1", Assert.Single(segments).Text);
    }

    [Fact]
    public async Task FinishAsync_APieceFails_MarksTheGap()
    {
        var transcriber = new FakeTranscriber
        {
            Respond = _ => Task.FromResult(new TranscriptionResult("", new(TranscriptionError.LocalFailed))),
        };
        var meeting = new MeetingTranscriber(transcriber, keepAudio: null);

        meeting.Add(Speaker.Others, Loud(2), TimeSpan.FromSeconds(2));
        var segments = await meeting.FinishAsync();

        Assert.Equal(MeetingSegment.Failed, Assert.Single(segments).Text);
    }

    [Fact]
    public async Task FinishAsync_SilenceOnly_TranscribesNothing()
    {
        var transcriber = Numbered();
        var meeting = new MeetingTranscriber(transcriber, keepAudio: null);

        meeting.Add(Speaker.You, Quiet(30), TimeSpan.FromSeconds(30));
        var segments = await meeting.FinishAsync();

        Assert.Empty(segments);
        Assert.Equal(0, transcriber.Calls);
    }

    [Fact]
    public async Task Add_TrackFallsBehindTheClock_PlacesLaterAudioWhereItWasHeard()
    {
        var meeting = new MeetingTranscriber(Numbered(), keepAudio: null);

        // The PC's sound sent nothing for a minute while nothing played.
        meeting.Add(Speaker.Others, [.. Loud(1), .. Quiet(2.5)], TimeSpan.FromSeconds(3.5));
        meeting.Add(Speaker.Others, [.. Loud(1), .. Quiet(2.5)], TimeSpan.FromSeconds(63.5));
        var segments = await meeting.FinishAsync();

        Assert.Equal(2, segments.Count);
        Assert.Equal(60, segments[1].Start, 0);
    }

    [Fact]
    public async Task FinishAsync_KeepAudio_StoresEachTranscribedPiece()
    {
        var kept = new List<int>();
        var meeting = new MeetingTranscriber(Numbered(), (n, _) => kept.Add(n));

        meeting.Add(Speaker.You, [.. Loud(1), .. Quiet(3), .. Loud(1)], TimeSpan.FromSeconds(5));
        await meeting.FinishAsync();

        Assert.Equal([1, 2], kept);
    }

    [Fact]
    public async Task Add_OneHourOfSpeech_KeepsOnlyUntranscribedAudioInMemory()
    {
        var transcriber = new CountingTranscriber();
        var meeting = new MeetingTranscriber(transcriber, keepAudio: null);

        // 8 s of speech and a 2.5 s pause, again and again: 120 MB of audio over the hour.
        byte[] stretch = [.. Loud(8), .. Quiet(2.5)];
        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var now = TimeSpan.Zero;
        while (now < TimeSpan.FromHours(1))
        {
            now += TimeSpan.FromSeconds(10.5);
            meeting.Add(Speaker.You, stretch, now);
        }

        var segments = await meeting.FinishAsync();
        var grown = GC.GetTotalMemory(forceFullCollection: true) - before;

        Assert.Equal(343, segments.Count);
        Assert.InRange(grown, long.MinValue, 20_000_000);
    }

    private static FakeTranscriber Numbered()
    {
        var count = 0;
        return new FakeTranscriber
        {
            Respond = _ => Task.FromResult(new TranscriptionResult($" piece {Interlocked.Increment(ref count)} ")),
        };
    }

    // Keeps nothing it is given, unlike FakeTranscriber, so only the meeting's memory is measured.
    private sealed class CountingTranscriber : ITranscriber
    {
        private int _count;

        public Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionResult($"piece {Interlocked.Increment(ref _count)}"));
    }
}
