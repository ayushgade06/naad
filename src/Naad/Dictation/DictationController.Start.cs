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

    // A press waiting out the start delay. Latch: the toggle joined it, so the recording runs hands-free.
    private sealed class PendingStart
    {
        public CancellationTokenSource Cancel { get; } = new();

        public bool Latch { get; set; }
    }
}
