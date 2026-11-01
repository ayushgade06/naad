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
/// <param name="microphone">The WinMM device number to record from.</param>
/// <param name="keepAudio">Whether to keep each meeting's recording (keepMeetingAudio).</param>
/// <param name="copy">Puts text on the clipboard; false if it was busy.</param>
/// <param name="capture">Opens the two tracks; tests replace it.</param>
internal sealed class MeetingRecorder(
    MeetingStore store,
    Func<ITranscriber?> transcriber,
    Func<bool> dictationIdle,
    Func<int> microphone,
    Func<bool> keepAudio,
    Func<string, Task<bool>> copy,
    Func<int, Action<Speaker, byte[]>, Action<bool>, Task<IMeetingCapture>>? capture = null)
{
    private IMeetingCapture? _capture;
    private MeetingTranscriber? _transcriber;
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
        try
        {
            tracks = await (capture ?? MeetingCapture.CreateAsync)(
                microphone(),
                (speaker, pcm) => pieces.Add(speaker, pcm, Stopwatch.GetElapsedTime(startedAt)),
                blocked => MicrophoneBlocked?.Invoke(blocked));
            if (tracks.Start() is { } failure)
            {
                tracks.Dispose();
                Log.Warn(LogEvent.MeetingCaptureFailed, ("error", (int)failure.Error));
                return MeetingOutcome.CaptureFailed;
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException
            or NotSupportedException or NAudio.MmException)
        {
            // Nothing may keep recording after a failed start.
            tracks?.Dispose();
            Log.Error(LogEvent.MeetingCaptureFailed, e);
            return MeetingOutcome.CaptureFailed;
        }
        finally
        {
            _starting = false;
        }

        (_capture, _transcriber, _id, _started, _startedAt) = (tracks, pieces, id, DateTimeOffset.Now, startedAt);
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

        _capture = null;
        _transcriber = null;
        var stop = FinishAsync(tracks, pieces, _id, _started, Stopwatch.GetElapsedTime(_startedAt));

        // A stop that finished at once has already cleared Finishing; it must stay clear.
        Finishing = stop.IsCompleted ? null : stop;
        return stop;
    }

    private async Task<MeetingOutcome> FinishAsync(
        IMeetingCapture tracks, MeetingTranscriber pieces, Guid id, DateTimeOffset started, TimeSpan duration)
    {
        try
        {
            await tracks.StopAsync();
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
            var copied = await copy(MeetingTranscript.ClipboardText(segments));
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
            Finishing = null;
        }
    }
}
