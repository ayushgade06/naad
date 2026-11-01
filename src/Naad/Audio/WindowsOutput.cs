using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Naad.Audio;

/// <summary>
/// The default playback device through Core Audio (NAudio.Wasapi). Every call opens the device
/// afresh, so a headset plugged in since is seen. A device that can't be read counts as none.
/// </summary>
internal sealed class WindowsOutput : IOutputDevice
{
    public OutputState? Default() => Read(e => e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));

    public OutputState? Find(string id) => Read(e => e.GetDevice(id));

    public bool SetMuted(string id, bool muted)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            device.AudioEndpointVolume.Mute = muted;
            return true;
        }
        catch (COMException e)
        {
            Log.Error(LogEvent.OutputMuteFailed, e);
            return false;
        }
    }

    private static OutputState? Read(Func<MMDeviceEnumerator, MMDevice> open)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = open(enumerator);
            var volume = device.AudioEndpointVolume;
            return new(device.ID, volume.Mute, volume.MasterVolumeLevelScalar);
        }
        catch (COMException e)
        {
            // No playback device at all is the usual reason; nothing to mute or warn about.
            Log.Warn(LogEvent.OutputMuteFailed, ("hresult", e.HResult));
            return null;
        }
    }
}
