using System.Diagnostics;

namespace Naad.Cleanup;

/// <summary>How much a transcript is tidied before it is pasted.</summary>
internal enum CleanupLevel
{
    /// <summary>Pasted as it was said.</summary>
    Off,

    /// <summary>Rule-based and instant (see <see cref="BasicCleanup"/>).</summary>
    Basic,

    /// <summary>By the language model, with the vocabulary in its prompt.</summary>
    Ai,
}

/// <summary>
/// Applies the chosen <see cref="CleanupLevel"/>, within <see cref="Budget"/>. Without a language
/// model, Ai cleans like Basic. A language model that fails, runs over the budget or gives an
/// unusable reply leaves the transcript as it was said, so a dictation is never lost to AI.
/// </summary>
/// <param name="level">The chosen level.</param>
/// <param name="ai">The language model step, or null when none is set up.</param>
/// <param name="budget">Tests shorten it; the app uses <see cref="Budget"/>.</param>
internal sealed class TranscriptCleanup(CleanupLevel level, AiCleanup? ai, TimeSpan? budget = null) : ITranscriptCleanup
{
    /// <summary>
    /// The longest a dictation waits for the language model: about twice the large model's time on
    /// a laptop processor for a long sentence, yet short enough not to feel stuck.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private static int _fallbacks;

    public bool UsesModel => ai is not null && (level == CleanupLevel.Ai || ai.Translates);

    public void Prepare()
    {
        if (UsesModel)
        {
            ai!.Prepare();
        }
    }

    public async Task<CleanupResult> CleanAsync(string transcript, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var (result, reason) = await RunAsync(transcript, cancellationToken);
        var milliseconds = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.Outcome == CleanupOutcome.FellBack)
        {
            // The count since Naad started shows how often AI cleanup is failing the user.
            Log.Warn(
                LogEvent.CleanupFellBack,
                ("reason", (int)reason),
                ("cleanup_ms", milliseconds),
                ("count", Interlocked.Increment(ref _fallbacks)));
        }
        else
        {
            Log.Info(
                LogEvent.CleanupCompleted,
                ("level", (int)level),
                ("outcome", (int)result.Outcome),
                ("cleanup_ms", milliseconds),
                ("chars_in", transcript.Length),
                ("chars_out", result.Text.Length));
        }

        return result;
    }

    private async Task<(CleanupResult, FallbackReason)> RunAsync(string transcript, CancellationToken cancellationToken)
    {
        var text = level == CleanupLevel.Off ? transcript : BasicCleanup.Clean(transcript);
        var outcome = level == CleanupLevel.Off ? CleanupOutcome.Unchanged : CleanupOutcome.Basic;
        if (!UsesModel)
        {
            return (new(text, outcome), FallbackReason.None);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget ?? Budget);
        try
        {
            // WaitAsync holds the budget even when the model can't stop at once, as while it loads.
            var running = level == CleanupLevel.Ai
                ? ai!.CleanAsync(transcript, timeout.Token)
                : ai!.TranslateAsync(text, timeout.Token);
            return await running.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (new(transcript, CleanupOutcome.FellBack), FallbackReason.Timeout);
        }
    }
}
