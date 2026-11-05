using Naad.Audio;

namespace Naad.Meetings;

/// <summary>Choosing and reading the meeting's microphone through WASAPI. Pure.</summary>
internal static class MeetingMicrophone
{
    // HRESULTs a capture start can fail with: access denied (privacy settings), the device gone,
    // and another app holding it exclusively.
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private const int DeviceInUse = unchecked((int)0x8889000A);
    private const int NotFoundHResult = unchecked((int)0x80070490);

    /// <summary>
    /// The index of the endpoint for <paramref name="chosen"/> among <paramref name="endpoints"/>'
    /// names, or -1 for the Windows default. Naad keeps the name as WinMM gives it, cut to 31
    /// characters, so an endpoint whose name starts with it matches; the first one wins.
    /// </summary>
    public static int Find(IReadOnlyList<string> endpoints, string? chosen)
    {
        if (string.IsNullOrEmpty(chosen))
        {
            return -1;
        }

        for (var i = 0; i < endpoints.Count; i++)
        {
            if (endpoints[i].StartsWith(chosen, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Why a WASAPI capture start failed, from its HRESULT.</summary>
    public static MicrophoneFailure FromHResult(int hresult) => new(
        hresult switch
        {
            AccessDenied => MicrophoneError.Blocked,
            DeviceInvalidated or NotFoundHResult => MicrophoneError.NotFound,
            DeviceInUse => MicrophoneError.InUse,
            _ => MicrophoneError.Failed,
        },
        hresult);
}
