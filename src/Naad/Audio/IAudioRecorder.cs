namespace Naad.Audio;

/// <summary>The microphone, behind a seam so dictation can be tested without one.</summary>
internal interface IAudioRecorder
{
    /// <summary>Starts recording, or returns why it can't. Returns null once recording.</summary>
    MicrophoneFailure? Start();

    /// <summary>
    /// Stops recording and returns the raw PCM in <see cref="AudioRecorder.Format"/>. Throws if
    /// the device reported an error while recording.
    /// </summary>
    Task<byte[]> StopAsync();
}
