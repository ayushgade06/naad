using Naad.Live;
using Naad.Tests.Dictation;
using Naad.Transcription;

namespace Naad.Tests.Live;

// The socket itself is checked by hand (M-LY3, M-LY4); this covers the fallback, offline.
[Collection("Log")]
public sealed class RealtimeTranscriptTests
{
    [Fact]
    public async Task FinishAsync_NeverConnected_UploadsTheWholeRecording()
    {
        var upload = new FakeTranscriber
        {
            Respond = _ => Task.FromResult(new TranscriptionResult("synthetic upload")),
        };
        using var stream = new RealtimeTranscript(LiveService.Deepgram, "synthetic-key", "nova-3", null, upload);
        var audio = SyntheticAudio.Loud(1);

        stream.Add(audio);
        var result = await stream.FinishAsync(audio, TestContext.Current.CancellationToken);

        Assert.Equal("synthetic upload", result.Text);
        Assert.Equal(audio.Length + 44, Assert.Single(upload.Uploads).Length);
    }
}
