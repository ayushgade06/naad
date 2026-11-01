using Naad.Audio;

namespace Naad.Meetings;

/// <summary>A meeting's two audio tracks, behind a seam so the recorder can be tested without devices.</summary>
internal interface IMeetingCapture : IDisposable
{
    /// <summary>Starts both tracks, or returns why the microphone can't open.</summary>
    MicrophoneFailure? Start();

    /// <summary>Stops both tracks once their last buffers have been delivered.</summary>
    Task StopAsync();
}
