using Naad.Audio;
using Naad.Cleanup;
using Naad.Context;
using Naad.History;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Transcription;

namespace Naad.Dictation;

/// <summary>
/// Hold-to-talk flow: press records, release transcribes and inserts. <see cref="DictationSession"/>
/// decides what happens; this class carries it out through its collaborators and holds no Win32,
/// HTTP or UI code. Runs on the UI thread: every await resumes there, so the session is only ever
/// touched from one thread. Logs events and numbers only, never what was said.
/// </summary>
/// <param name="recorder">The microphone.</param>
/// <param name="transcriber">Turns the recording into text; null until an API key is set. See
/// <see cref="UseTranscriber"/>.</param>
/// <param name="inserter">Puts the text into the target window.</param>
/// <param name="captureTarget">The window that has focus, read when the shortcut is pressed.</param>
/// <param name="checkTarget">Whether that window may receive text now.</param>
/// <param name="time">Measures holds and durations; tests replace it.</param>
/// <param name="pressEnter">Presses Enter in the foreground window, for the "press enter" command.</param>
internal sealed partial class DictationController(
    IAudioRecorder recorder,
    ITranscriber? transcriber,
    TextInserter inserter,
    Func<WindowTarget> captureTarget,
    Func<WindowTarget, InsertionDecision> checkTarget,
    TimeProvider time,
    Func<bool>? pressEnter = null) : IDisposable
{
    // At the time limit the user learns from the stop tone or the icon that recording ended, then
    // lets go of the shortcut. Held longer than this, the transcript is kept instead.
    private static readonly TimeSpan TimeLimitReleaseTimeout = TimeSpan.FromSeconds(30);

    private readonly DictationSession _session = new();
    private readonly CancellationTokenSource _shutdown = new();
    private ITranscriber? _transcriber = transcriber;
    private readonly TranscriptDelivery _delivery = new(inserter, checkTarget, pressEnter ?? (() => false), time);
    private ITranscriptCleanup? _cleanup;
    private Func<WindowTarget, Task<DictationContext>>? _readContext;
    private Task<DictationContext> _context = Task.FromResult(DictationContext.None);
    private Func<Task<DictationContext>, CancellationToken, Task<ContextSummary>>? _summarize;
    private Task<ContextSummary> _summary = Task.FromResult(ContextSummary.None);
    private CancellationTokenSource? _summaryCancel;

    // The summary runs while the user speaks; after release it gets this much longer at most, so it
    // never holds up the paste for long.
    private static readonly TimeSpan SummaryWait = TimeSpan.FromSeconds(1);
    private CancellationTokenSource? _jobCancel;
    private bool _latched;

    // The last text a dictation produced, for Paste again. Memory only.
    private string? _lastText;
    private Task<bool> _starting = Task.FromResult(false);

    // Completes once the last recording has fully stopped. A new recording waits for it, so a
    // quick press after a discarded one never opens the microphone while it is still closing.
    private Task _stopped = Task.CompletedTask;
    private WindowTarget _target = WindowTarget.None;
    private bool _disposed;


    /// <summary>The dictation state changed. Raised on the UI thread.</summary>
    public event Action<DictationState>? StateChanged;

    /// <summary>The microphone is open and recording. Raised on the UI thread.</summary>
    public event Action? MicrophoneOpened;

    /// <summary>The microphone of a recording that opened is closed again. Raised on the UI thread.</summary>
    public event Action? MicrophoneClosed;

    /// <summary>The microphone could not be opened for a dictation. Raised on the UI thread.</summary>
    public event Action<MicrophoneError>? MicrophoneFailed;

    /// <summary>A recording held no sound worth uploading. Raised on the UI thread.</summary>
    public event Action? NothingHeard;

    /// <summary>
    /// The shortcut was pressed but there is no transcriber, so nothing was recorded: the user
    /// must add an API key first. Raised on the UI thread.
    /// </summary>
    public event Action? TranscriberMissing;

    /// <summary>
    /// The language model is cleaning up the transcript, or rewriting the selection when the
    /// argument is true. Raised on the UI thread.
    /// </summary>
    public event Action<bool>? CleaningUp;

    /// <summary>
    /// Cleanup failed, ran out of time or gave an unusable reply, so the transcript is pasted as
    /// it was said. Raised on the UI thread.
    /// </summary>
    public event Action? CleanupFellBack;

    /// <summary>The transcription service failed. Raised on the UI thread.</summary>
    public event Action<TranscriptionError>? TranscriptionFailed;

    /// <summary>
    /// A transcript wasn't inserted and is kept for <see cref="CopyKeptTranscriptAsync"/>. Raised
    /// on the UI thread.
    /// </summary>
    public event Action<TranscriptKeptReason>? TranscriptKept
    {
        add => _delivery.Kept += value;
        remove => _delivery.Kept -= value;
    }

    /// <summary>
    /// A dictation ended with text, or with a failed transcription, for the history. Raised on the
    /// UI thread. The report holds what was said: never log it.
    /// </summary>
    public event Action<DictationReport>? Finished;

    /// <summary>An edit couldn't be made: the selection is left as it was. Raised on the UI thread.</summary>
    public event Action<bool>? EditFailed;

    public DictationState State => _session.State;

    /// <summary>The hold-to-talk shortcut went down. Call on the UI thread.</summary>
    public void Press() => ApplyShortcut(DictationEvent.Pressed);

    /// <summary>The hold-to-talk shortcut was released. Call on the UI thread.</summary>
    public void Release() => ApplyShortcut(DictationEvent.Released);

    /// <summary>Another key or a mouse button joined the hold-to-talk shortcut. Call on the UI thread.</summary>
    public void Cancel(CancelCause cause) => ApplyShortcut(
        cause == CancelCause.Mouse ? DictationEvent.CancelledByMouse : DictationEvent.CancelledByKey);

    /// <summary>
    /// Transcribes with <paramref name="replacement"/> from the next recording on, after a settings
    /// or key change. A transcription already running finishes with the one it started with. Call
    /// on the UI thread.
    /// </summary>
    public void UseTranscriber(ITranscriber? replacement) => _transcriber = replacement;

    /// <summary>
    /// Tidies transcripts with <paramref name="replacement"/> from the next dictation on; null
    /// pastes them as said. Call on the UI thread.
    /// </summary>
    public void UseCleanup(ITranscriptCleanup? replacement) => _cleanup = replacement;


    /// <summary>
    /// Stops accepting new dictations and cancels a transcription or paste in flight, so nothing
    /// is inserted once shutdown starts. Dispose before the recorder it uses.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelPendingStart();
        TakeLive()?.Dispose();
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    private void ApplyNow(DictationEvent e)
    {
        // A latched recording maps its next press onto the release that ends a held dictation, so
        // the session's rules (short-hold check, time limit, cancels) stay the same in every mode.
        if (_latched)
        {
            if (e == DictationEvent.Released)
            {
                return;
            }

            if (e == DictationEvent.Pressed && _session.State == DictationState.Recording)
            {
                e = DictationEvent.Released;
            }
        }

        if (e == DictationEvent.Pressed && _session.State == DictationState.Idle && RefusedBusy())
        {
            return;
        }

        // Without a transcriber the words could never be used, so the microphone isn't opened.
        if (e == DictationEvent.Pressed && _transcriber is null && _session.State == DictationState.Idle)
        {
            Log.Warn(LogEvent.DictationNeedsApiKey);
            TranscriberMissing?.Invoke();
            return;
        }

        Apply(e, _session.Job);
    }

    private DictationStep Apply(DictationEvent e, long job)
    {
        var before = _session.State;
        var step = _session.Handle(e, job, time.GetElapsedTime(0));

        // Raised before the action runs: an action can finish synchronously and apply further
        // events, whose changes must be reported after this one, not before it.
        if (_session.State != before)
        {
            StateChanged?.Invoke(_session.State);
        }

        switch (step.Action)
        {
            case DictationAction.StartRecording:
                _target = _prereadTarget ?? captureTarget();
                _latched = false;

                // Read while the user speaks; the reader keeps its own time limit.
                _context = _preread ?? _readContext?.Invoke(_target) ?? Task.FromResult(DictationContext.None);
                _ = ReportEditAsync(_context, step.Job);
                StartSummary();
                _cleanup?.Prepare();
                StartLive();
                _starting = StartRecordingAsync(step.Job, _stopped, time.GetTimestamp());
                _ = StopAtMaxDurationAsync(step.Job, _shutdown.Token);
                break;
            case DictationAction.Discard:
                TakeLive()?.Dispose();

                // What was read and summed up goes with the recording thrown away.
                _context = Task.FromResult(DictationContext.None);
                StopSummary();
                _ = DiscardAsync(e, step.Held);
                break;
            case DictationAction.Transcribe:
                _ = TranscribeAsync(step, time.GetTimestamp(), atTimeLimit: e == DictationEvent.MaxDurationReached);
                break;
        }

        return step;
    }

    private async Task DiscardAsync(DictationEvent cause, TimeSpan held)
    {
        try
        {
            if (await StopRecording() is not null)
            {
                DictationLog.Discarded(cause, held);
            }
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
        }
    }

    private async Task TranscribeAsync(DictationStep step, long releasedAt, bool atTimeLimit)
    {
        // Read before the first await: Dispose may dispose the source later, and a transcriber
        // swapped in meanwhile is for the next dictation.
        using var job = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _jobCancel = job;
        var cancel = job.Token;
        var service = _transcriber;
        var cleanup = _cleanup;
        var contextTask = _context;
        var summaryTask = _summary;
        using var live = TakeLive();

        // The window the user was in when they pressed the shortcut, never whatever has focus now.
        var target = _target;
        byte[]? wav = null;
        var report = new DictationReport(target.ProcessId);
        try
        {
            // Null: the microphone never opened, and the session already ended the job.
            if (await StopRecording() is not { } pcm)
            {
                return;
            }

            // Silence and slivers of audio are never uploaded: Whisper invents text for them.
            // Returning ends the job in the finally block below.
            var check = SilenceGate.Check(pcm, AudioRecorder.Format);
            if (check.Verdict != RecordingVerdict.Accepted)
            {
                DictationLog.Rejected(check);
                if (check.Verdict == RecordingVerdict.Silent)
                {
                    NothingHeard?.Invoke();
                }

                return;
            }

            wav = Wav.Encode(pcm, AudioRecorder.Format);
            report = report with { AudioMilliseconds = Milliseconds(check.Duration) };
            Log.Info(
                LogEvent.RecordingStopped,
                ("held_ms", Milliseconds(step.Held)),
                ("wav_bytes", wav.Length),
                ("peak", check.Peak),
                ("rms", check.Rms));

            var requestStart = time.GetTimestamp();
            // A recording only starts with a transcriber, and one is never removed mid-dictation.
            var result = await TranscribeAsync(
                service ?? throw new InvalidOperationException("No transcriber."), live, pcm, wav, cancel);
            report = report with { TranscribeMilliseconds = Milliseconds(time.GetElapsedTime(requestStart)) };
            if (live?.Session.FellBack == true)
            {
                report = report with { LiveFellBack = true };
                LiveFellBack?.Invoke();
            }

            if (result.Failure is { } failure)
            {
                DictationLog.Transcription(failure);
                TranscriptionFailed?.Invoke(failure.Error);
                Finished?.Invoke(report with { Outcome = HistoryOutcome.Failed, Wav = wav });
                return;
            }

            var raw = result.Text;
            var text = raw;
            var thenEnter = false;
            Log.Info(
                LogEvent.TranscriptionCompleted, ("request_ms", report.TranscribeMilliseconds), ("chars", raw.Length));

            // Paste again has this dictation's words from here on, as said: Esc during cleanup or a
            // failed edit must never leave the previous dictation's text behind.
            var previousText = _lastText;
            if (raw.Length > 0)
            {
                _lastText = raw;
            }

            var context = await ContextOrNoneAsync(contextTask);
            // Without a selection to edit, or a model to edit it, the selection stays as it was.
            if (context.NeedsSelection || (context.IsEdit && cleanup is null))
            {
                Log.Warn(LogEvent.EditNeedsSelection);
                EditFailed?.Invoke(context.NeedsSelection);
                return;
            }

            // Once live typing has put words in, the rest is typed too; with nothing typed yet (the
            // shortcut was held throughout, say) the text is cleaned up and pasted as usual.
            live?.Typer?.StopIfNothingTyped();
            if (live?.Typer is { Typed.Length: > 0 } typer && !context.IsEdit && raw.Length > 0)
            {
                if (Apply(DictationEvent.Transcribed, step.Job).Action == DictationAction.Insert)
                {
                    var typed = await FinishTypedAsync(typer, raw, cancel);
                    Finished?.Invoke(report with { Raw = raw, Text = raw, Outcome = typed, Wav = wav });
                }

                return;
            }

            if (raw.Length > 0 && cleanup is not null)
            {
                // Only now: a dictation pasted as said, or typed live, has no use for it.
                context = await WithSummaryAsync(context, summaryTask, cancel);
                var cleanStart = time.GetTimestamp();
                var cleaned = await CleanAsync(cleanup, raw, context, cancel);
                report = report with { CleanupMilliseconds = Milliseconds(time.GetElapsedTime(cleanStart)) };
                if (cleaned.Outcome == CleanupOutcome.EditFailed)
                {
                    EditFailed?.Invoke(false);
                    return;
                }

                (text, thenEnter) = (cleaned.Text, cleaned.PressEnter);
            }

            if (text.Length == 0)
            {
                // Only "press enter" was said: a command, not words to paste again.
                if (thenEnter)
                {
                    _lastText = previousText;
                    _delivery.PressEnter(target);
                }

                Log.Info(LogEvent.TranscriptionEmpty);
                Apply(DictationEvent.TranscribedEmpty, step.Job);
                return;
            }

            // A late or repeated result for an ended job is refused here, so nothing is
            // inserted twice.
            if (Apply(DictationEvent.Transcribed, step.Job).Action != DictationAction.Insert)
            {
                return;
            }

            // At the time limit the shortcut is still held, and a modifier shortcut such as Right
            // Ctrl would make the paste give up at once; so it waits for the user to let go.
            if (atTimeLimit)
            {
                await inserter.WaitForModifiersReleasedAsync(TimeLimitReleaseTimeout, cancel);
            }

            _lastText = text;
            var pasted = await _delivery.DeliverAsync(text, target, releasedAt, thenEnter, cancel);
            Finished?.Invoke(report with
            {
                Raw = raw,
                Text = text,
                Selection = context.Selection,
                Invocation = context.Invocation,
                Outcome = pasted ? HistoryOutcome.Pasted : HistoryOutcome.Kept,
                Wav = wav,
            });
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Log.Info(_shutdown.IsCancellationRequested
                ? LogEvent.DictationCancelledByShutdown
                : LogEvent.DictationCancelledByEscape);
        }
        catch (Exception e)
        {
            // Fire-and-forget: anything not caught here would vanish silently.
            Log.Error(LogEvent.DictationFailed, e);
        }
        finally
        {
            _jobCancel = null;

            // What was read and summed up belongs to this dictation only; a screenshot isn't kept.
            if (_context == contextTask)
            {
                _context = Task.FromResult(DictationContext.None);
                StopSummary();
            }

            // Every path ends the job, so the next press is accepted. Events for a job that
            // already ended are ignored by the session.
            var end = _session.State == DictationState.Inserting
                ? DictationEvent.Inserted
                : DictationEvent.TranscriptionFailed;
            Apply(end, step.Job);
        }
    }

    private static long Milliseconds(TimeSpan duration) => DictationLog.Milliseconds(duration);
}
