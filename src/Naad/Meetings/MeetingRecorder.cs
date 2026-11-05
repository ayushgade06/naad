using System.Diagnostics;
using Naad.Transcription;

namespace Naad.Meetings;

/// <summary>
/// Starts and stops meeting notes: both tracks are captured and transcribed in pieces while the
/// meeting runs; on stop the transcript is saved encrypted and copied to the clipboard. Never
/// runs during a dictation, and busy from the start of opening the tracks until the last piece
/// is saved, so nothing new starts over a meeting still finishing. Use on the UI thread. Logs
/// events and counts, never what was said.
/// </summary>
/// <param name="store">Where meetings are kept.</param>
/// <param name="transcriber">The transcriber in use, or null.</param>
/// <param name="dictationIdle">Whether no dictation is running.</param>
/// <param name="microphone">The microphone's name as Windows lists it, or null for the default.</param>
/// <param name="keepAudio">Whether to keep each meeting's recording (keepMeetingAudio).</param>
/// <param name="copy">Puts text on the clipboard; false if it was busy.</param>
/// <param name="capture">Opens the two tracks; tests replace it.</param>
internal sealed class MeetingRecorder(
    MeetingStore store,
    Func<ITranscriber?> transcriber,
    Func<bool> dictationIdle,
    Func<string?> microphone,
    Func<bool> keepAudio,
    Func<string, Task<bool>> copy,
    Func<string?, Action<Speaker, byte[]>, Action<bool>, Task<IMeetingCapture>>? capture = null)
{
    private IMeetingCapture? _capture;
    private MeetingTranscriber? _transcriber;
    private MeetingTranscriber? _shown;
    private TimeSpan _ran;
    private double _youLevel;
    private double _othersLevel;
    private long _youAt;
    private long _othersAt;
    private volatile bool _blocked;
    private Guid _id;
    private DateTimeOffset _started;
    private long _startedAt;
    private bool _starting;

    /// <summary>A meeting is being recorded. Raised on the UI thread.</summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>
    /// The microphone went silent or away mid-meeting (true), or works again (false). Raised on a
    /// capture or thread-pool thread.
    /// </summary>
    public event Action<bool>? MicrophoneBlocked;

    public bool IsRecording => _capture is not null;

    /// <summary>How long the meeting in progress has run; frozen at its stop while it finishes.</summary>
    public TimeSpan Elapsed => IsRecording ? Stopwatch.GetElapsedTime(_startedAt) : _ran;

    // The PC's sound delivers nothing while nothing plays: a level older than this is silence.
    private static readonly TimeSpan LevelFresh = TimeSpan.FromMilliseconds(300);

    /// <summary>The louder track's level, 0 to 1, for the live card's meter; 0 when not recording.</summary>
    public double Level =>
        IsRecording ? Math.Max(Fresh(ref _youLevel, ref _youAt), Fresh(ref _othersLevel, ref _othersAt)) : 0;

    /// <summary>The microphone is silent or away right now.</summary>
    public bool MicrophoneIsBlocked => IsRecording && _blocked;

    /// <summary>The lines of the meeting recording or finishing, in time order; none otherwise.</summary>
    public IReadOnlyList<MeetingSegment> Lines => _shown?.Lines() ?? [];

    /// <summary>How many lines <see cref="Lines"/> has, without copying them.</summary>
    public int LineCount => _shown?.LineCount ?? 0;

    /// <summary>The last stop, while its pieces are still being transcribed and saved; null otherwise.</summary>
    public Task<MeetingOutcome>? Finishing { get; private set; }

    /// <summary>Opening, recording or finishing a meeting: the microphone and transcriber are in use.</summary>
    public bool IsBusy => _starting || IsRecording || Finishing is not null;

    /// <summary>
    /// Starts recording a meeting, unless one is busy, a dictation runs or nothing can transcribe it.
    /// </summary>
    public async Task<MeetingOutcome> StartAsync()
    {
        if (IsBusy)
        {
            return MeetingOutcome.Unchanged;
        }

        if (!dictationIdle())
        {
            return MeetingOutcome.DictationRunning;
        }

        if (transcriber() is not { } service)
        {
            return MeetingOutcome.NoTranscriber;
        }

        var id = Guid.NewGuid();
        var keep = keepAudio();
        var pieces = new MeetingTranscriber(service, keep ? (n, wav) => store.SaveAudio(id, n, wav) : null);
        var startedAt = Stopwatch.GetTimestamp();
        IMeetingCapture? tracks = null;
        _starting = true;
        _blocked = false;
        (_shown, _ran) = (null, TimeSpan.Zero);
        (_youLevel, _othersLevel) = (0, 0);

        // One meter per track, each used only on its own capture thread.
        var you = new Audio.LevelMeter();
        var others = new Audio.LevelMeter();
        try
        {
            tracks = await (capture ?? MeetingCapture.CreateAsync)(
                microphone(),
                (speaker, pcm) =>
                {
                    pieces.Add(speaker, pcm, Stopwatch.GetElapsedTime(startedAt));
                    var duration = TimeSpan.FromSeconds(
                        pcm.Length / (double)Audio.AudioRecorder.Format.AverageBytesPerSecond);
                    var level = (speaker == Speaker.You ? you : others).Add(Audio.LevelMeter.Rms(pcm), duration);
                    Volatile.Write(ref speaker == Speaker.You ? ref _youLevel : ref _othersLevel, level);
                    Volatile.Write(
                        ref speaker == Speaker.You ? ref _youAt : ref _othersAt, Stopwatch.GetTimestamp());
                },
                blocked =>
                {
                    _blocked = blocked;
                    MicrophoneBlocked?.Invoke(blocked);
                });
            if (tracks.Start() is { } failure)
            {
                tracks.Dispose();
                Log.Warn(LogEvent.MeetingCaptureFailed, ("error", (int)failure.Error));
                return MeetingOutcome.CaptureFailed;
            }
        }
        catch (Exception e)
        {
            // Nothing may keep recording after a failed start, whatever Windows threw.
            tracks?.Dispose();
            Log.Error(LogEvent.MeetingCaptureFailed, e);
            return MeetingOutcome.CaptureFailed;
        }
        finally
        {
            _starting = false;
        }

        (_capture, _transcriber, _shown, _id, _started, _startedAt) =
            (tracks, pieces, pieces, id, DateTimeOffset.Now, startedAt);
        Log.Info(LogEvent.MeetingStarted, ("keep_audio", keep ? 1 : 0));
        RecordingChanged?.Invoke(true);
        return MeetingOutcome.Started;
    }

    /// <summary>
    /// Stops the meeting, waits for the last buffers and pieces, then saves and copies the
    /// transcript. The last piece is never dropped: each track's rest is transcribed too. A stop
    /// while one is finishing returns that one.
    /// </summary>
    public Task<MeetingOutcome> StopAsync()
    {
        if (Finishing is { } finishing)
        {
            return finishing;
        }

        if (_capture is not { } tracks || _transcriber is not { } pieces)
        {
            return Task.FromResult(MeetingOutcome.Unchanged);
        }

        _ran = Stopwatch.GetElapsedTime(_startedAt);
        _capture = null;
        _transcriber = null;
        var stop = FinishAsync(tracks, pieces, _id, _started, _ran);

        // A stop that finished at once has already cleared Finishing; it must stay clear.
        Finishing = stop.IsCompleted ? null : stop;
        return stop;
    }

    private async Task<MeetingOutcome> FinishAsync(
        IMeetingCapture tracks, MeetingTranscriber pieces, Guid id, DateTimeOffset started, TimeSpan duration)
    {
        try
        {
            try
            {
                await tracks.StopAsync();
            }
            catch (Exception e)
            {
                // A device that refused to stop is still let go, and what was heard is still saved.
                Log.Error(LogEvent.MeetingCaptureFailed, e);
            }

            tracks.Dispose();
            RecordingChanged?.Invoke(false);

            var segments = await pieces.FinishAsync();
            Log.Info(LogEvent.MeetingStopped, ("segments", segments.Count), ("seconds", (long)duration.TotalSeconds));
            if (segments.Count == 0)
            {
                store.Delete(id);
                return MeetingOutcome.NothingHeard;
            }

            var meeting = new MeetingRecord(
                id, MeetingRecord.DefaultTitle(started.LocalDateTime), started, duration, segments);
            var saved = store.Save(meeting);
            bool copied;
            try
            {
                copied = await copy(MeetingTranscript.ClipboardText(segments));
            }
            catch (Exception e)
            {
                // Saved already: a clipboard failure mustn't report the meeting as lost.
                Log.Error(LogEvent.TranscriptCopyFailed, e);
                copied = false;
            }

            return (saved, copied) switch
            {
                (true, true) => MeetingOutcome.Copied,
                (false, true) => MeetingOutcome.CopiedNotSaved,
                (true, false) => MeetingOutcome.SavedNotCopied,
                _ => MeetingOutcome.NotSaved,
            };
        }
        finally
        {
            // The text goes from memory with the meeting; the page reads the saved copy.
            _shown = null;
            Finishing = null;
        }
    }

    private static double Fresh(ref double level, ref long at) =>
        Stopwatch.GetElapsedTime(Volatile.Read(ref at)) < LevelFresh ? Volatile.Read(ref level) : 0;
}
