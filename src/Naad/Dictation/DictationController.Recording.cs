using Naad.Audio;

namespace Naad.Dictation;

// Opening and closing the microphone for a recording, and its time limit. The flow that drives
// them is in DictationController.cs.
internal sealed partial class DictationController
{
    // Opening the microphone can take long enough that the keyboard hook, which shares
    // this thread, misses the key release of a quick tap. So it opens on the thread pool.
    private async Task<bool> StartRecordingAsync(long job, Task previousStop, long pressedAt)
    {
        // The previous recording's own task reports how its stop went.
        await previousStop.ConfigureAwait(
            ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        MicrophoneFailure? failure;
        try
        {
            failure = await Task.Run(recorder.Start);
        }
        catch (ObjectDisposedException)
        {
            // Naad is shutting down: the recorder was disposed while this press was queued.
            return false;
        }

        if (failure is { } f)
        {
            DictationLog.Microphone(f);
            Apply(DictationEvent.RecordingFailed, job);
            MicrophoneFailed?.Invoke(f.Error);
            return false;
        }

        Log.Info(LogEvent.RecordingStarted, ("mic_open_ms", Milliseconds(time.GetElapsedTime(pressedAt))));
        MicrophoneOpened?.Invoke();
        return true;
    }

    // Returns the recorded PCM, or null if the microphone never opened.
    private Task<byte[]?> StopRecording()
    {
        var stop = StopAfterStartAsync(_starting);
        _stopped = stop;
        return stop;
    }

    private async Task<byte[]?> StopAfterStartAsync(Task<bool> starting)
    {
        if (!await starting)
        {
            return null;
        }

        try
        {
            return await recorder.StopAsync();
        }
        finally
        {
            MicrophoneClosed?.Invoke();
        }
    }

    // ponytail: every recording leaves a five-minute timer behind, ignored by job id once the
    // recording ends; cancel it per job if many short dictations ever show up in memory use.
    private async Task StopAtMaxDurationAsync(long job, CancellationToken shutdown)
    {
        try
        {
            await Task.Delay(DictationSession.MaxRecording, time, shutdown);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Apply(DictationEvent.MaxDurationReached, job).Action == DictationAction.Transcribe)
        {
            Log.Info(LogEvent.RecordingMaxDurationReached);
        }
    }
}
