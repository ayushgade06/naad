using Naad.Context;
using Naad.Insertion;

namespace Naad.Dictation;

// How shortcut presses start a recording: the hands-free toggle, and the start delay that lets a
// quick tap of hold to talk record nothing. The session itself only ever sees a start once it
// happens, so the tray, the overlay and the recording flag never see a press that came to nothing.
internal sealed partial class DictationController
{
    private PendingStart? _pendingStart;
    private TimeSpan _startDelay;

    // The job the toggle's own press started, so keys joining that press cancel it, as with a hold.
    private long? _toggleStartedJob;

    // A Manual edit is checked for a selection before recording; that read is then the recording's.
    private Func<EditIntent>? _editIntent;
    private Task<DictationContext>? _preread;
    private WindowTarget? _prereadTarget;

    /// <summary>
    /// Whether a press asks for a Manual edit, read at once when it comes; null for never. Call on
    /// the UI thread.
    /// </summary>
    public void UseEditIntent(Func<EditIntent>? intent) => _editIntent = intent;

    /// <summary>
    /// A press of hold to talk is waiting out the start delay, or no longer is. Raised on the UI
    /// thread; Esc should cancel while it waits.
    /// </summary>
    public event Action<bool>? StartPending;

    /// <summary>No dictation runs or is about to start.</summary>
    public bool IsIdle => _session.State == DictationState.Idle && _pendingStart is null;

    /// <summary>
    /// The toggle shortcut went down: starts a hands-free dictation at once, stops the one
    /// running, or turns a held one hands-free. Call on the UI thread.
    /// </summary>
    // A toggle has no release to tell a tap from a hold, so the start delay doesn't apply: keys
    // joining the press cancel it instead (CancelToggle).
    public void PressToggle()
    {
        if (_disposed)
        {
            return;
        }

        _toggleStartedJob = null;
        if (_pendingStart is { } pending)
        {
            pending.Latch = true;
        }
        else if (_session.State == DictationState.Recording && !_latched)
        {
            Latch();
        }
        else if (_session.State == DictationState.Idle)
        {
            Start(latch: true, delayed: false);
            if (_session.State == DictationState.Recording)
            {
                _toggleStartedJob = _session.Job;
            }
        }
        else
        {
            // A latched recording's next press stops it, as a release ends a held one.
            ApplyShortcut(DictationEvent.Pressed);
        }
    }

    /// <summary>
    /// Another key joined the toggle shortcut as it went down, as in Right Ctrl+C: a recording
    /// that press started is thrown away. Call on the UI thread.
    /// </summary>
    public void CancelToggle()
    {
        if (_toggleStartedJob is { } job && job == _session.Job && _session.State == DictationState.Recording)
        {
            _toggleStartedJob = null;
            ApplyNow(DictationEvent.CancelledByKey);
        }
    }

    /// <summary>
    /// How long hold to talk must stay down before recording starts, so a quick tap records
    /// nothing. Zero starts at once. Call on the UI thread.
    /// </summary>
    public void UseStartDelay(TimeSpan delay) => _startDelay = delay;

    // Shortcut events always apply to the current job; after shutdown they are ignored.
    private void ApplyShortcut(DictationEvent e)
    {
        if (_disposed)
        {
            return;
        }

        // Let go, or joined by another key, before the start delay ran out: nothing records.
        if (_pendingStart is not null)
        {
            if (e != DictationEvent.Pressed)
            {
                CancelPendingStart();
            }

            return;
        }

        if (e == DictationEvent.Pressed && _session.State == DictationState.Idle)
        {
            _toggleStartedJob = null;
            Start(latch: false, delayed: true);
            return;
        }

        ApplyNow(e);
    }

    // A press while idle: after the start delay, unless it is skipped or zero.
    private void Start(bool latch, bool delayed)
    {
        if (delayed && _startDelay > TimeSpan.Zero)
        {
            var pending = new PendingStart { Latch = latch };
            _pendingStart = pending;
            StartPending?.Invoke(true);
            _ = StartAfterDelayAsync(pending);
            return;
        }

        // A Manual edit with nothing selected is refused before the microphone opens; a press refused
        // anyway (meeting notes, no transcriber) goes straight on, so nothing is read for it.
        if (_preread is null && _editIntent?.Invoke() == EditIntent.EditRequired && _readContext is { } reader
            && _busy?.Invoke() != true && _transcriber is not null)
        {
            var check = new PendingStart { Latch = latch };
            _pendingStart = check;
            StartPending?.Invoke(true);
            var target = captureTarget();
            _ = CheckSelectionAsync(check, target, reader(target));
            return;
        }

        ApplyNow(DictationEvent.Pressed);
        if (latch && _session.State == DictationState.Recording)
        {
            _latched = true;
            WentHandsFree?.Invoke();
        }
    }

    private async Task StartAfterDelayAsync(PendingStart pending)
    {
        try
        {
            await Task.Delay(_startDelay, time, pending.Cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_pendingStart != pending)
        {
            return;
        }

        _pendingStart = null;
        pending.Cancel.Dispose();
        StartPending?.Invoke(false);
        Start(pending.Latch, delayed: false);
    }

    private async Task CheckSelectionAsync(PendingStart check, WindowTarget target, Task<DictationContext> read)
    {
        var context = await ContextOrNoneAsync(read);

        // Let go or cancelled meanwhile: nothing starts.
        if (_pendingStart != check)
        {
            return;
        }

        _pendingStart = null;
        check.Cancel.Dispose();
        StartPending?.Invoke(false);
        if (context.NeedsSelection)
        {
            Log.Warn(LogEvent.EditNeedsSelection);
            EditFailed?.Invoke(true);
            return;
        }

        // The recording is for the window that was checked, even if the focus moved meanwhile.
        (_preread, _prereadTarget) = (Task.FromResult(context), target);
        Start(check.Latch, delayed: false);
        (_preread, _prereadTarget) = (null, null);
    }

    private void CancelPendingStart()
    {
        if (_pendingStart is { } pending)
        {
            _pendingStart = null;
            pending.Cancel.Cancel();
            pending.Cancel.Dispose();
            Log.Info(LogEvent.DictationStartCancelled);
            StartPending?.Invoke(false);
        }
    }

    // A recording starts: the summary of its context starts too, and any earlier one stops.
    private void StartSummary()
    {
        StopSummary();
        if (_summarize is null)
        {
            _summary = Task.FromResult(ContextSummary.None);
            return;
        }

        _summaryCancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _summary = _summarize(_context, _summaryCancel.Token);
    }

    private void StopSummary()
    {
        _summaryCancel?.Cancel();
        _summaryCancel?.Dispose();
        _summaryCancel = null;
        _summary = Task.FromResult(ContextSummary.None);
    }

    // A press waiting out the start delay. Latch: the toggle joined it, so the recording runs hands-free.
    private sealed class PendingStart
    {
        public CancellationTokenSource Cancel { get; } = new();

        public bool Latch { get; set; }
    }
}
