namespace Naad.Meetings;

/// <summary>What the tray and the Meetings page do with meeting notes. Use on the UI thread.</summary>
/// <param name="recorder">Records meetings.</param>
/// <param name="store">The saved meetings.</param>
internal sealed class MeetingActions(MeetingRecorder recorder, MeetingStore store)
{
    private bool _exiting;

    public MeetingRecorder Recorder => recorder;

    public MeetingStore Store => store;

    /// <summary>A start or stop finished, with how it went; also when a meeting finished saving.</summary>
    public event Action<MeetingOutcome>? Finished;

    /// <summary>Starts meeting notes, or stops and saves them when they are recording.</summary>
    public async Task ToggleAsync()
    {
        var stopping = recorder.IsRecording;
        MeetingOutcome outcome;
        try
        {
            outcome = stopping ? await recorder.StopAsync() : await recorder.StartAsync();
        }
        catch (Exception e)
        {
            // Fire-and-forget from a click: anything not caught here would vanish silently, and
            // the page and tray would wait for an answer that never comes.
            Log.Error(LogEvent.MeetingCaptureFailed, e);
            outcome = stopping ? MeetingOutcome.NotSaved : MeetingOutcome.CaptureFailed;
        }

        Finished?.Invoke(outcome);
    }

    /// <summary>
    /// Quits Naad with <paramref name="exit"/> once a meeting still recording or finishing is
    /// saved, so quitting never loses the end of a meeting. A second call while waiting does nothing.
    /// </summary>
    public async Task ExitAsync(Action exit)
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        if (recorder.IsRecording)
        {
            await ToggleAsync();
        }

        try
        {
            if (recorder.Finishing is { } finishing)
            {
                await finishing;
            }
        }
        catch (Exception e)
        {
            // Quitting goes ahead; the failure is logged, as a click's would be.
            Log.Error(LogEvent.MeetingWriteFailed, e);
        }

        exit();
    }

    /// <summary>Deletes every saved meeting and its recordings, at once.</summary>
    public void DeleteAll()
    {
        store.DeleteAll();
        Log.Info(LogEvent.MeetingsCleared);
    }
}
