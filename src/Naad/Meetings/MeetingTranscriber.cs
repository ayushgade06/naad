using System.Buffers;
using Naad.Audio;
using Naad.Live;
using Naad.Transcription;

namespace Naad.Meetings;

/// <summary>
/// Transcribes a meeting piece by piece while it runs: each track is split at pauses, and each
/// piece is transcribed in turn, so memory holds only the pieces not yet transcribed, however
/// long the meeting. A piece that fails becomes <see cref="MeetingSegment.Failed"/>, so the gap
/// shows. <see cref="Add"/> may be called from both capture threads; transcription runs on the
/// thread pool, one piece at a time. Logs counts only.
/// </summary>
/// <param name="transcriber">Transcribes each piece; on this PC by default.</param>
/// <param name="keepAudio">Stores each piece's recording, when the user keeps meeting audio; null
/// keeps none.</param>
internal sealed class MeetingTranscriber(ITranscriber transcriber, Action<int, byte[]>? keepAudio)
{
    // A pause of 2 s ends a piece; after 10 s any quiet moment does; 15 s is the longest piece.
    // Long enough for Whisper to have context, short enough to finish soon after the meeting.
    // Once the other track has spoken since a piece began, 0.6 s ends it, so each turn is its own line.
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TurnPause = TimeSpan.FromSeconds(0.6);

    // The other side speaking within this long of a piece's start is crosstalk at the turn, not a new turn.
    private const double TurnGraceSeconds = 0.5;
    private static readonly TimeSpan Target = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(15);

    private readonly Track _you = new(Speaker.You);
    private readonly Track _others = new(Speaker.Others);
    private readonly List<MeetingSegment> _segments = [];
    private readonly Lock _gate = new();
    private Task _work = Task.CompletedTask;
    private volatile bool _finished;
    private int _pieces;
    private int _failed;

    /// <summary>
    /// The next audio of one track, as 16 kHz mono PCM16, which arrived <paramref name="now"/>
    /// after the meeting started.
    /// </summary>
    public void Add(Speaker speaker, byte[] pcm, TimeSpan now)
    {
        var track = speaker == Speaker.You ? _you : _others;
        var other = speaker == Speaker.You ? _others : _you;
        lock (track)
        {
            // A device that delivered after the stop timed out: the meeting is already being saved.
            if (_finished)
            {
                return;
            }

            foreach (var (start, piece) in track.Add(pcm, now, other.LastVoiceSeconds))
            {
                Queue(speaker, start, piece);
            }
        }
    }

    /// <summary>How many lines have been transcribed so far. Safe from any thread.</summary>
    public int LineCount
    {
        get
        {
            lock (_segments)
            {
                return _segments.Count;
            }
        }
    }

    /// <summary>The lines transcribed so far, in time order. Safe from any thread.</summary>
    public IReadOnlyList<MeetingSegment> Lines()
    {
        lock (_segments)
        {
            return [.. _segments.OrderBy(s => s.Start)];
        }
    }

    /// <summary>
    /// Transcribes what is left of each track and returns every segment in time order, once all
    /// pieces are done. Call once, after the last <see cref="Add"/>.
    /// </summary>
    public async Task<IReadOnlyList<MeetingSegment>> FinishAsync()
    {
        foreach (var track in new[] { _you, _others })
        {
            lock (track)
            {
                _finished = true;
                if (track.Flush() is var (start, piece))
                {
                    Queue(track.Speaker, start, piece);
                }
            }
        }

        Task work;
        lock (_gate)
        {
            work = _work;
        }

        await work;
        Log.Info(LogEvent.MeetingTranscribed, ("pieces", _pieces), ("failed", _failed));
        lock (_segments)
        {
            return [.. _segments.OrderBy(s => s.Start)];
        }
    }

    // ponytail: pieces wait in the continuation chain until transcribed, so memory stays flat
    // only while transcription keeps up with two tracks; on a slow PC the backlog grows. Bound
    // the queue (or drop to one track) if long meetings on the processor show it.
    private void Queue(Speaker speaker, double start, byte[] pcm)
    {
        // A piece of noise is never sent: it has no words, and Whisper invents some.
        if (SilenceGate.Check(pcm, AudioRecorder.Format).Verdict != RecordingVerdict.Accepted)
        {
            return;
        }

        lock (_gate)
        {
            var number = ++_pieces;
            _work = _work.ContinueWith(
                _ => TranscribeAsync(number, speaker, start, pcm), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task TranscribeAsync(int number, Speaker speaker, double start, byte[] pcm)
    {
        var wav = Wav.Encode(pcm, AudioRecorder.Format);
        string text;
        try
        {
            keepAudio?.Invoke(number, wav);
            var result = await transcriber.TranscribeAsync(wav, CancellationToken.None);
            text = result.Failure is null ? result.Text.Trim() : MeetingSegment.Failed;
        }
        catch (Exception e)
        {
            // A chain of continuations: anything not caught here would vanish silently.
            Log.Error(LogEvent.MeetingPieceFailed, e);
            text = MeetingSegment.Failed;
        }

        if (text == MeetingSegment.Failed)
        {
            Interlocked.Increment(ref _failed);
        }

        if (text.Length > 0)
        {
            lock (_segments)
            {
                _segments.Add(new(speaker, start, text));
            }
        }
    }

    // One track's audio since its last piece, placed on the meeting's timeline. The PC's sound
    // delivers nothing while nothing plays, so when a track falls behind the clock, the piece in
    // progress ends and the track jumps ahead, instead of holding the silence in memory.
    private sealed class Track(Speaker speaker)
    {
        // A track behind the clock by more than this skipped some time; less is buffering.
        // ponytail: only a track behind is corrected, so a device clock that runs fast drifts
        // ahead of the other track by its error (a few seconds an hour at worst); resync in both
        // directions, at a pause, if turns are seen out of order.
        private const double MaxLagSeconds = 1;

        private static readonly double BytesPerSecond = AudioRecorder.Format.AverageBytesPerSecond;

        private readonly ArrayBufferWriter<byte> _pending = new();
        private SpeechChunker _chunker = NewChunker();
        private long _chunkerStart;
        private long _chunked;
        private long _pendingStart;
        private double _lastVoice = double.NegativeInfinity;

        public Speaker Speaker => speaker;

        /// <summary>When this track last had voice, in seconds into the meeting. Read from the other track's
        /// thread.</summary>
        public double LastVoiceSeconds => Volatile.Read(ref _lastVoice);

        /// <param name="pcm">The next audio.</param>
        /// <param name="now">When it arrived, from the meeting's start.</param>
        /// <param name="otherVoice">When the other track last had voice, in seconds into the meeting.</param>
        public List<(double Start, byte[] Pcm)> Add(byte[] pcm, TimeSpan now, double otherVoice)
        {
            var pieces = new List<(double, byte[])>();
            var lag = now.TotalSeconds - ((_chunkerStart + _chunked + pcm.Length) / BytesPerSecond);
            if (lag > MaxLagSeconds)
            {
                if (Flush() is { } piece)
                {
                    pieces.Add(piece);
                }

                // Whole samples, so the audio stays aligned.
                _chunkerStart = Math.Max(_pendingStart, (long)((now.TotalSeconds * BytesPerSecond) - pcm.Length) & ~1L);
                _pendingStart = _chunkerStart;
                _chunked = 0;
                _chunker = NewChunker();
            }

            _pending.Write(pcm);
            _chunked += pcm.Length;
            // The other side spoke well after this piece began: the turn has passed. Their last words
            // overlapping our first don't count.
            // ponytail: the tracks' clocks agree only to within MaxLagSeconds, more than the turn
            // pause, so a skewed device can split a turn late or early; compare capture timestamps if
            // turns are seen merged or split wrongly.
            _chunker.OtherSpeaking =
                otherVoice > ((_chunkerStart + _chunker.Cut) / BytesPerSecond) + TurnGraceSeconds;
            foreach (var cut in _chunker.Add(pcm))
            {
                pieces.Add(Take(_chunkerStart + cut));
            }

            if (_chunker.LastVoiced > 0)
            {
                Volatile.Write(ref _lastVoice, (_chunkerStart + _chunker.LastVoiced) / BytesPerSecond);
            }

            // Quiet before anyone speaks is dropped, so a piece starts when its words do.
            var quietUntil = _chunkerStart + _chunker.Cut;
            if (quietUntil > _pendingStart)
            {
                var rest = _pending.WrittenSpan[(int)(quietUntil - _pendingStart)..].ToArray();
                _pending.ResetWrittenCount();
                _pending.Write(rest);
                _pendingStart = quietUntil;
            }

            return pieces;
        }

        private static SpeechChunker NewChunker() => new(Pause, Longest, Target, TurnPause);

        public (double Start, byte[] Pcm)? Flush() =>
            _pending.WrittenCount == 0 ? null : Take(_pendingStart + _pending.WrittenCount);

        private (double, byte[]) Take(long end)
        {
            var length = (int)(end - _pendingStart);
            var piece = _pending.WrittenSpan[..length].ToArray();
            var rest = _pending.WrittenSpan[length..].ToArray();
            _pending.ResetWrittenCount();
            _pending.Write(rest);
            // A piece starts when its words do, not with the quiet before them.
            var skip = 0;
            while (skip + SpeechChunker.WindowBytes <= piece.Length
                && LevelMeter.Rms(piece.AsSpan(skip, SpeechChunker.WindowBytes)) < SpeechChunker.QuietRms)
            {
                skip += SpeechChunker.WindowBytes;
            }

            var start = (_pendingStart + skip) / BytesPerSecond;
            _pendingStart = end;
            return (start, piece[skip..]);
        }
    }
}
