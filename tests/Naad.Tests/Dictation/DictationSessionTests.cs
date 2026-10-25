using Naad.Dictation;

namespace Naad.Tests.Dictation;

public sealed class DictationSessionTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    // Every state against every event, for the current job. Shortcut events ignore the job.
    // Rows name the enum members because a public test method can't take internal enums.
    [Theory]
    [InlineData("Idle", "Pressed", "Recording", "StartRecording")]
    [InlineData("Idle", "Released", "Idle", "None")]
    [InlineData("Idle", "CancelledByKey", "Idle", "None")]
    [InlineData("Idle", "CancelledByMouse", "Idle", "None")]
    [InlineData("Idle", "MaxDurationReached", "Idle", "None")]
    [InlineData("Idle", "RecordingFailed", "Idle", "None")]
    [InlineData("Idle", "Transcribed", "Idle", "None")]
    [InlineData("Idle", "TranscribedEmpty", "Idle", "None")]
    [InlineData("Idle", "TranscriptionFailed", "Idle", "None")]
    [InlineData("Idle", "Inserted", "Idle", "None")]
    [InlineData("Recording", "Pressed", "Recording", "None")]
    [InlineData("Recording", "Released", "Transcribing", "Transcribe")]
    [InlineData("Recording", "CancelledByKey", "Idle", "Discard")]
    [InlineData("Recording", "CancelledByMouse", "Idle", "Discard")]
    [InlineData("Recording", "MaxDurationReached", "Transcribing", "Transcribe")]
    [InlineData("Recording", "RecordingFailed", "Idle", "None")]
    [InlineData("Recording", "Transcribed", "Recording", "None")]
    [InlineData("Recording", "TranscribedEmpty", "Recording", "None")]
    [InlineData("Recording", "TranscriptionFailed", "Recording", "None")]
    [InlineData("Recording", "Inserted", "Recording", "None")]
    [InlineData("Transcribing", "Pressed", "Transcribing", "None")]
    [InlineData("Transcribing", "Released", "Transcribing", "None")]
    [InlineData("Transcribing", "CancelledByKey", "Transcribing", "None")]
    [InlineData("Transcribing", "CancelledByMouse", "Transcribing", "None")]
    [InlineData("Transcribing", "MaxDurationReached", "Transcribing", "None")]
    [InlineData("Transcribing", "RecordingFailed", "Idle", "None")]
    [InlineData("Transcribing", "Transcribed", "Inserting", "Insert")]
    [InlineData("Transcribing", "TranscribedEmpty", "Idle", "None")]
    [InlineData("Transcribing", "TranscriptionFailed", "Idle", "None")]
    [InlineData("Transcribing", "Inserted", "Transcribing", "None")]
    [InlineData("Inserting", "Pressed", "Inserting", "None")]
    [InlineData("Inserting", "Released", "Inserting", "None")]
    [InlineData("Inserting", "CancelledByKey", "Inserting", "None")]
    [InlineData("Inserting", "CancelledByMouse", "Inserting", "None")]
    [InlineData("Inserting", "MaxDurationReached", "Inserting", "None")]
    [InlineData("Inserting", "RecordingFailed", "Inserting", "None")]
    [InlineData("Inserting", "Transcribed", "Inserting", "None")]
    [InlineData("Inserting", "TranscribedEmpty", "Inserting", "None")]
    [InlineData("Inserting", "TranscriptionFailed", "Inserting", "None")]
    [InlineData("Inserting", "Inserted", "Idle", "None")]
    public void Handle_StateAndEvent_MovesToExpectedStateWithAction(
        string from, string e, string expectedState, string expectedAction)
    {
        var session = SessionIn(Enum.Parse<DictationState>(from));

        var step = session.Handle(Enum.Parse<DictationEvent>(e), session.Job, 10 * Second);

        Assert.Equal(Enum.Parse<DictationState>(expectedState), session.State);
        Assert.Equal(Enum.Parse<DictationAction>(expectedAction), step.Action);
    }

    [Fact]
    public void Handle_TableCoversEveryStateAndEventPair()
    {
        var rows = typeof(DictationSessionTests)
            .GetMethod(nameof(Handle_StateAndEvent_MovesToExpectedStateWithAction))!
            .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
            .Cast<InlineDataAttribute>()
            .Select(row => (
                Enum.Parse<DictationState>((string)row.Data[0]!),
                Enum.Parse<DictationEvent>((string)row.Data[1]!)))
            .ToList();
        var all = Enum.GetValues<DictationState>()
            .SelectMany(state => Enum.GetValues<DictationEvent>().Select(e => (state, e)))
            .ToHashSet();

        Assert.Equal(all.Count, rows.Count);
        Assert.Equal(all, rows.ToHashSet());
    }

    // A result for an older job changes nothing, in every state where the current job's
    // result would have counted.
    [Theory]
    [InlineData("Recording", "MaxDurationReached")]
    [InlineData("Recording", "RecordingFailed")]
    [InlineData("Transcribing", "RecordingFailed")]
    [InlineData("Transcribing", "Transcribed")]
    [InlineData("Transcribing", "TranscribedEmpty")]
    [InlineData("Transcribing", "TranscriptionFailed")]
    [InlineData("Inserting", "Inserted")]
    public void Handle_ResultForOldJob_IsIgnored(string state, string e)
    {
        var session = SessionIn(Enum.Parse<DictationState>(state));

        var step = session.Handle(Enum.Parse<DictationEvent>(e), session.Job - 1, 10 * Second);

        Assert.Equal(Enum.Parse<DictationState>(state), session.State);
        Assert.Equal(DictationAction.None, step.Action);
    }

    [Theory]
    [InlineData(0, "Discard", "Idle")]
    [InlineData(299, "Discard", "Idle")]
    [InlineData(300, "Transcribe", "Transcribing")]
    [InlineData(5000, "Transcribe", "Transcribing")]
    public void Handle_Release_DiscardsHoldsShorterThanMinimum(int heldMs, string action, string state)
    {
        var session = new DictationSession();
        session.Handle(DictationEvent.Pressed, 0, Second);

        var step = session.Handle(DictationEvent.Released, session.Job, Second + TimeSpan.FromMilliseconds(heldMs));

        Assert.Equal(Enum.Parse<DictationAction>(action), step.Action);
        Assert.Equal(Enum.Parse<DictationState>(state), session.State);
        Assert.Equal(TimeSpan.FromMilliseconds(heldMs), step.Held);
    }

    [Fact]
    public void Handle_Press_StartsANewJobEachTime()
    {
        var session = new DictationSession();

        var first = session.Handle(DictationEvent.Pressed, 0, Second);
        session.Handle(DictationEvent.CancelledByKey, 0, 2 * Second);
        var second = session.Handle(DictationEvent.Pressed, 0, 3 * Second);

        Assert.Equal(1, first.Job);
        Assert.Equal(2, second.Job);
    }

    [Fact]
    public void Handle_TranscriptArrivesTwice_InsertsOnce()
    {
        var session = SessionIn(DictationState.Transcribing);

        var first = session.Handle(DictationEvent.Transcribed, session.Job, 10 * Second);
        var again = session.Handle(DictationEvent.Transcribed, session.Job, 10 * Second);
        session.Handle(DictationEvent.Inserted, session.Job, 10 * Second);
        var afterInsert = session.Handle(DictationEvent.Transcribed, session.Job, 10 * Second);

        Assert.Equal(DictationAction.Insert, first.Action);
        Assert.Equal(DictationAction.None, again.Action);
        Assert.Equal(DictationAction.None, afterInsert.Action);
        Assert.Equal(DictationState.Idle, session.State);
    }

    [Fact]
    public void Handle_MaxDurationOfAnEarlierRecording_DoesNotStopTheCurrentOne()
    {
        var session = new DictationSession();
        var first = session.Handle(DictationEvent.Pressed, 0, Second);
        session.Handle(DictationEvent.Released, session.Job, 2 * Second);
        session.Handle(DictationEvent.TranscriptionFailed, session.Job, 3 * Second);
        session.Handle(DictationEvent.Pressed, 0, 4 * Second);

        // The first recording's five-minute timer fires during the second recording.
        var step = session.Handle(DictationEvent.MaxDurationReached, first.Job, DictationSession.MaxRecording);

        Assert.Equal(DictationAction.None, step.Action);
        Assert.Equal(DictationState.Recording, session.State);
    }

    [Fact]
    public void Handle_MaxDuration_TranscribesAndIgnoresTheLaterRelease()
    {
        var session = new DictationSession();
        session.Handle(DictationEvent.Pressed, 0, Second);

        var stop = session.Handle(
            DictationEvent.MaxDurationReached, session.Job, Second + DictationSession.MaxRecording);
        var release = session.Handle(DictationEvent.Released, session.Job, 2 * DictationSession.MaxRecording);

        Assert.Equal(DictationAction.Transcribe, stop.Action);
        Assert.Equal(DictationSession.MaxRecording, stop.Held);
        Assert.Equal(DictationAction.None, release.Action);
        Assert.Equal(DictationState.Transcribing, session.State);
    }

    // Reaches a state the way the controller would, with a hold long enough to transcribe.
    private static DictationSession SessionIn(DictationState state)
    {
        var session = new DictationSession();
        if (state == DictationState.Idle)
        {
            return session;
        }

        session.Handle(DictationEvent.Pressed, 0, Second);
        if (state != DictationState.Recording)
        {
            session.Handle(DictationEvent.Released, session.Job, 3 * Second);
        }

        if (state == DictationState.Inserting)
        {
            session.Handle(DictationEvent.Transcribed, session.Job, 5 * Second);
        }

        Assert.Equal(state, session.State);
        return session;
    }
}
