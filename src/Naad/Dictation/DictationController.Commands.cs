using Naad.Cleanup;
using Naad.Context;
using Naad.Insertion;

namespace Naad.Dictation;

// The commands beyond hold-to-talk: the tray's start and stop, Esc, the hands-free latch, Paste
// again, copying, the context reader, and the guarded cleanup call. The flow they act on is in
// DictationController.cs.
internal sealed partial class DictationController
{
    private Func<bool>? _busy;

    /// <summary>A press was refused because meeting notes are recording. Raised on the UI thread.</summary>
    public event Action? RefusedWhileBusy;

    /// <summary>
    /// The recording runs hands-free: letting go of the shortcut no longer stops it. Raised on the
    /// UI thread.
    /// </summary>
    public event Action? WentHandsFree;

    /// <summary>The recording is an Edit Mode instruction, not a dictation. Raised on the UI thread.</summary>
    public event Action? EditRecording;

    /// <summary>The context model gave no summary, and why. Raised on the UI thread.</summary>
    public event Action<ContextFailure>? ContextFailed;

    /// <summary>Esc cancelled a recording or a transcription in flight. Raised on the UI thread.</summary>
    public event Action? Cancelled;

    /// <summary>
    /// From the tray menu: starts a dictation, or stops the one recording. A click is neither a
    /// press nor a hold, so a held dictation ends with a release. Call on the UI thread.
    /// </summary>
    public void Toggle()
    {
        if (_disposed || _pendingStart is not null)
        {
            return;
        }

        // A click starts at once: there is no tap to tell apart from a hold.
        if (_session.State == DictationState.Idle)
        {
            Start(latch: true, delayed: false);
        }
        else
        {
            ApplyNow(_session.State == DictationState.Recording && !_latched
                ? DictationEvent.Released
                : DictationEvent.Pressed);
        }
    }

    /// <summary>
    /// Reads the app's context, and its selection for Edit Mode, when a dictation starts; null
    /// reads nothing. The reader must keep its own time limit. Call on the UI thread.
    /// </summary>
    public void UseContextReader(Func<WindowTarget, Task<DictationContext>>? reader) => _readContext = reader;

    /// <summary>
    /// Sums up what the user is doing from the context, started with each recording; null for no
    /// summary. Call on the UI thread.
    /// </summary>
    public void UseContextSummary(Func<Task<DictationContext>, CancellationToken, Task<ContextSummary>>? summarize) =>
        _summarize = summarize;

    // The summary, once the dictation needs it: what is ready within a moment, or nothing.
    private async Task<DictationContext> WithSummaryAsync(
        DictationContext context, Task<ContextSummary> summary, CancellationToken cancel)
    {
        ContextSummary ready;
        try
        {
            ready = await summary.WaitAsync(SummaryWait, cancel);
        }
        catch (TimeoutException)
        {
            // Stopped, not left running: a model on this PC answers one prompt at a time, and
            // cleanup must not queue behind a summary nobody will read.
            Log.Info(LogEvent.ContextSummaryLate);
            StopSummary();
            return context;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error(LogEvent.ContextSummaryFailed, e);
            return context;
        }

        if (ready.Failure is { } failure)
        {
            ContextFailed?.Invoke(failure);
        }

        return context with { Summary = ready.Text };
    }

    /// <summary>
    /// Puts the kept transcript on the clipboard, out of clipboard history, for the user to
    /// paste themselves. Naad never pastes it: the user may be anywhere by then, and the tray
    /// menu itself takes focus. Returns false if there is none or the clipboard is busy.
    /// Call on the UI thread.
    /// </summary>
    public Task<bool> CopyKeptTranscriptAsync() => _delivery.CopyKeptAsync();

    /// <summary>Puts <paramref name="text"/> on the clipboard, as from the history. Call on the UI thread.</summary>
    public Task<bool> CopyAsync(string text) => _delivery.CopyAsync(text);

    /// <summary>
    /// Pastes the last dictation's text again, into the window that has focus now. Only between
    /// dictations. Call on the UI thread.
    /// </summary>
    public Task<bool> PasteAgainAsync() => PasteAsync(_lastText);

    /// <summary>Pastes <paramref name="text"/> into the window that has focus now. Call on the UI thread.</summary>
    public async Task<bool> PasteAsync(string? text)
    {
        if (text is null || _disposed || _session.State != DictationState.Idle)
        {
            return false;
        }

        var pasted = await _delivery.DeliverAsync(
            text, captureTarget(), time.GetTimestamp(), thenEnter: false, _shutdown.Token);
        Log.Info(LogEvent.PastedAgain, ("pasted", pasted ? 1 : 0));
        return pasted;
    }

    /// <summary>
    /// Esc during a dictation: a recording is thrown away, and a transcription or cleanup in
    /// flight is cancelled, with nothing pasted. Call on the UI thread.
    /// </summary>
    public void Escape()
    {
        if (_disposed)
        {
            return;
        }

        Log.Info(LogEvent.DictationCancelledByEscape, ("state", (int)_session.State));
        if (_pendingStart is not null)
        {
            CancelPendingStart();
            return;
        }

        if (_session.State == DictationState.Recording)
        {
            Apply(DictationEvent.CancelledByKey, _session.Job);
        }
        else if (_session.State == DictationState.Transcribing)
        {
            _jobCancel?.Cancel();
        }
        else
        {
            return;
        }

        Cancelled?.Invoke();
    }

    /// <summary>
    /// A key joined the held shortcut to latch the recording on: letting go no longer stops it, and
    /// the next press does. Call on the UI thread.
    /// </summary>
    public void Latch()
    {
        if (_pendingStart is { } pending)
        {
            pending.Latch = true;
        }
        else if (!_disposed && _session.State == DictationState.Recording)
        {
            _latched = true;
            WentHandsFree?.Invoke();
            Log.Info(LogEvent.DictationLatched);
        }
    }

    // Never loses the transcript: a cleanup that throws is treated like one that fell back.
    private async Task<CleanupResult> CleanAsync(
        ITranscriptCleanup cleanup, string text, DictationContext context, CancellationToken shutdown)
    {
        if (cleanup.UsesModel || context.IsEdit)
        {
            CleaningUp?.Invoke(context.IsEdit);
        }

        try
        {
            var cleaned = await cleanup.CleanAsync(text, context, shutdown);
            if (cleaned.Outcome == CleanupOutcome.FellBack)
            {
                CleanupFellBack?.Invoke();
            }

            return cleaned;
        }
        catch (Exception e) when (e is not OperationCanceledException || !shutdown.IsCancellationRequested)
        {
            Log.Error(LogEvent.CleanupFellBack, e);

            // An edit that throws leaves the selection alone, like one that fails.
            if (context.IsEdit)
            {
                return new("", CleanupOutcome.EditFailed);
            }

            CleanupFellBack?.Invoke();
            return new(text, CleanupOutcome.FellBack);
        }
    }

    /// <summary>
    /// Something else has the microphone, such as meeting notes, so a press starts nothing. Call
    /// on the UI thread.
    /// </summary>
    public void UseBusy(Func<bool>? busy) => _busy = busy;

    // A press while busy is refused and said so: the meeting already records the user.
    private bool RefusedBusy()
    {
        if (_busy?.Invoke() != true)
        {
            return false;
        }

        Log.Info(LogEvent.DictationRefusedDuringMeeting);
        RefusedWhileBusy?.Invoke();
        return true;
    }

    // A context read that fails costs the hints, never the transcript.
    private static async Task<DictationContext> ContextOrNoneAsync(Task<DictationContext> context)
    {
        try
        {
            return await context;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Error(LogEvent.ContextReadFailed, e);
            return DictationContext.None;
        }
    }

    // Says so as soon as the context read shows the recording edits a selection, while it records.
    private async Task ReportEditAsync(Task<DictationContext> context, long job)
    {
        try
        {
            if ((await context).IsEdit && _session.Job == job && _session.State == DictationState.Recording)
            {
                EditRecording?.Invoke();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The dictation reads it again and goes on without it.
            Log.Error(LogEvent.ContextReadFailed, e);
        }
    }
}
