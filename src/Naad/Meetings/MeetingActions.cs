namespace Naad.Meetings;

/// <summary>What the tray and the Meetings page do with meeting notes. Use on the UI thread.</summary>
/// <param name="recorder">Records meetings.</param>
/// <param name="store">The saved meetings.</param>
/// <param name="copy">Puts text on the clipboard, out of clipboard history.</param>
/// <param name="languageModel">The language model in use, for summaries; null when none can run.</param>
internal sealed class MeetingActions(
    MeetingRecorder recorder,
    MeetingStore store,
    Func<string, Task<bool>> copy,
    Func<LanguageModel.ILanguageModel?> languageModel)
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

    /// <summary>Whether a language model can write summaries now.</summary>
    public bool CanSummarize => languageModel() is not null;

    /// <summary>Puts <paramref name="text"/> on the clipboard; false if it was busy.</summary>
    public Task<bool> CopyAsync(string text) => copy(text);

    /// <summary>
    /// Saves <paramref name="meeting"/> under a new title; null when the title is blank or unchanged.
    /// </summary>
    public MeetingRecord? Rename(MeetingRecord meeting, string typed)
    {
        if (meeting.Renamed(typed) is not { } title)
        {
            return null;
        }

        if (store.Update(meeting.Id, m => m with { Title = title }) is not { } renamed)
        {
            return null;
        }

        Log.Info(LogEvent.MeetingRenamed);
        return renamed;
    }

    /// <summary>
    /// Writes and saves the meeting's summary; null if no model can run, it failed, or the meeting
    /// was deleted meanwhile. Only the summary is changed, so a rename made while it was being
    /// written stays.
    /// </summary>
    public async Task<MeetingRecord?> SummarizeAsync(MeetingRecord meeting, CancellationToken cancellationToken)
    {
        if (languageModel() is not { } model
            || await new MeetingSummarizer(model).SummarizeAsync(meeting, cancellationToken) is not { } summary)
        {
            return null;
        }

        return store.Update(meeting.Id, m => m with { Summary = summary });
    }

    /// <summary>Deletes one meeting and its recordings, at once.</summary>
    public void Delete(MeetingRecord meeting)
    {
        store.Delete(meeting.Id);
        Log.Info(LogEvent.MeetingDeleted);
    }

    /// <summary>Deletes every saved meeting and its recordings, at once.</summary>
    public void DeleteAll()
    {
        store.DeleteAll();
        Log.Info(LogEvent.MeetingsCleared);
    }
}
