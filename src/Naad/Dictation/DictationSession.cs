namespace Naad.Dictation;

internal enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Inserting,
}

internal enum DictationEvent
{
    /// <summary>The shortcut was pressed.</summary>
    Pressed,

    /// <summary>The shortcut was released.</summary>
    Released,

    /// <summary>Another key joined the shortcut, as in Right Ctrl+C.</summary>
    CancelledByKey,

    /// <summary>A mouse button or the wheel was used while the shortcut was held.</summary>
    CancelledByMouse,

    /// <summary>The recording reached <see cref="DictationSession.MaxRecording"/>.</summary>
    MaxDurationReached,

    /// <summary>The microphone could not be opened.</summary>
    RecordingFailed,

    /// <summary>The transcript arrived and has text.</summary>
    Transcribed,

    /// <summary>The transcript arrived empty.</summary>
    TranscribedEmpty,

    /// <summary>
    /// Transcription failed, timed out or was cancelled, or the recording was rejected before upload.
    /// </summary>
    TranscriptionFailed,

    /// <summary>Insertion finished, whether or not the text could be pasted.</summary>
    Inserted,
}

internal enum DictationAction
{
    /// <summary>No side effect. The event may still have ended the job.</summary>
    None,

    StartRecording,

    /// <summary>Stop the recording and throw the audio away.</summary>
    Discard,

    /// <summary>Stop the recording and transcribe it.</summary>
    Transcribe,

    /// <summary>Insert the transcript.</summary>
    Insert,
}

/// <param name="Action">What the controller must do now.</param>
/// <param name="Job">The job the action belongs to.</param>
/// <param name="Held">For a stopped recording, how long it ran.</param>
internal readonly record struct DictationStep(DictationAction Action, long Job, TimeSpan Held = default);

/// <summary>
/// The one owner of dictation state: Idle, Recording, Transcribing, Inserting. Pure: it never
/// touches the microphone, the network or the clipboard; the controller does what each
/// <see cref="DictationStep"/> says. Every press starts a new job with a new id. Results that
/// arrive asynchronously carry their job's id and count only for the current job in the
/// expected state, so a late or repeated result can never insert a transcript twice.
/// </summary>
internal sealed class DictationSession
{
    /// <summary>
    /// Shorter holds are taps or shortcuts such as Right Ctrl+C; Whisper tends to invent text for them.
    /// </summary>
    public static readonly TimeSpan MinimumHold = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Groq rejects uploads above 25 MB; 16 kHz mono PCM16 reaches that after about 13 minutes,
    /// so 5 minutes leaves a wide margin and bounds the memory a forgotten hold can use.
    /// </summary>
    public static readonly TimeSpan MaxRecording = TimeSpan.FromMinutes(5);

    private TimeSpan _pressedAt;

    public DictationState State { get; private set; }

    /// <summary>The id of the current job, or of the last one when idle. The first job is 1.</summary>
    public long Job { get; private set; }

    /// <summary>
    /// Applies one event and returns what to do. Events that don't fit the state, such as a press
    /// while transcribing, are ignored.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="job">For asynchronous results, the job they belong to. Shortcut events
    /// (press, release, cancels) always apply to the current job and ignore it.</param>
    /// <param name="now">A monotonic timestamp, used to measure the hold.</param>
    public DictationStep Handle(DictationEvent e, long job, TimeSpan now)
    {
        var current = job == Job;
        switch (State, e)
        {
            case (DictationState.Idle, DictationEvent.Pressed):
                Job++;
                _pressedAt = now;
                State = DictationState.Recording;
                return new(DictationAction.StartRecording, Job);

            case (DictationState.Recording, DictationEvent.Released):
                var held = now - _pressedAt;
                var tooShort = held < MinimumHold;
                State = tooShort ? DictationState.Idle : DictationState.Transcribing;
                return new(tooShort ? DictationAction.Discard : DictationAction.Transcribe, Job, held);

            case (DictationState.Recording, DictationEvent.CancelledByKey or DictationEvent.CancelledByMouse):
                State = DictationState.Idle;
                return new(DictationAction.Discard, Job, now - _pressedAt);

            case (DictationState.Recording, DictationEvent.MaxDurationReached) when current:
                State = DictationState.Transcribing;
                return new(DictationAction.Transcribe, Job, now - _pressedAt);

            // The microphone opens in the background, so it can fail after the release.
            case (DictationState.Recording or DictationState.Transcribing, DictationEvent.RecordingFailed) when current:
                State = DictationState.Idle;
                return new(DictationAction.None, Job);

            case (DictationState.Transcribing, DictationEvent.Transcribed) when current:
                State = DictationState.Inserting;
                return new(DictationAction.Insert, Job);

            case (DictationState.Transcribing, DictationEvent.TranscribedEmpty or DictationEvent.TranscriptionFailed)
                when current:
            case (DictationState.Inserting, DictationEvent.Inserted) when current:
                State = DictationState.Idle;
                return new(DictationAction.None, Job);

            default:
                return new(DictationAction.None, Job);
        }
    }
}
