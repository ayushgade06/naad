using NAudio;

namespace Naad.Audio;

internal enum MicrophoneError
{
    /// <summary>Windows reports no recording device.</summary>
    NotFound,

    /// <summary>Windows privacy settings deny Naad the microphone.</summary>
    Blocked,

    /// <summary>Another app holds the device exclusively.</summary>
    InUse,

    /// <summary>Any other failure to open the device.</summary>
    Failed,
}

/// <summary>What Windows' privacy settings allow, as Settings shows it.</summary>
internal enum MicrophoneAccess
{
    Allowed,
    OffForDevice,
    OffForUser,
    OffForDesktopApps,
    Unknown,
}

/// <summary>Why recording could not start.</summary>
/// <param name="Error">The kind of failure, which decides what the user is told.</param>
/// <param name="Code">The WinMM result code, for the log, when Windows returned one; otherwise 0.</param>
internal readonly record struct MicrophoneFailure(MicrophoneError Error, int Code = 0)
{
    /// <summary>Maps a failed waveInOpen or waveInStart result to a failure.</summary>
    public static MicrophoneFailure FromResult(MmResult result) => new(
        result switch
        {
            MmResult.BadDeviceId or MmResult.NoDriver => MicrophoneError.NotFound,
            MmResult.AlreadyAllocated or MmResult.HandleBusy => MicrophoneError.InUse,
            _ => MicrophoneError.Failed,
        },
        (int)result);

    /// <summary>
    /// Whether Windows' microphone consent values deny desktop apps: the device-wide switch, the
    /// user's "Microphone access", and "Let desktop apps access your microphone". Windows stores
    /// each as "Allow" or "Deny"; a missing value means allowed. A blocked microphone may still
    /// open and record silence, so this is checked before opening it.
    /// </summary>
    public static bool IsDeniedByConsent(IEnumerable<string?> consentValues) =>
        consentValues.Any(value => string.Equals(value, "Deny", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What Settings says about microphone access, from the device-wide switch, the user's
    /// "Microphone access" and "Let desktop apps access your microphone"; null values for
    /// <paramref name="readable"/> false mean they couldn't be read.
    /// </summary>
    public static MicrophoneAccess Access(bool readable, string? device, string? user, string? desktopApps)
    {
        static bool Denied(string? value) => string.Equals(value, "Deny", StringComparison.OrdinalIgnoreCase);
        return !readable ? MicrophoneAccess.Unknown
            : Denied(device) ? MicrophoneAccess.OffForDevice
            : Denied(user) ? MicrophoneAccess.OffForUser
            : Denied(desktopApps) ? MicrophoneAccess.OffForDesktopApps
            : MicrophoneAccess.Allowed;
    }

    /// <summary>The words Settings shows for <paramref name="access"/>.</summary>
    public static string Describe(MicrophoneAccess access) => access switch
    {
        MicrophoneAccess.Allowed => "Allowed: Windows lets Naad use the microphone.",
        MicrophoneAccess.OffForDevice => "Off for this PC: an administrator turned the microphone off in Windows.",
        MicrophoneAccess.OffForUser => "Off: Microphone access is turned off in Windows privacy settings.",
        MicrophoneAccess.OffForDesktopApps => "Off: \"Let desktop apps access your microphone\" is turned off.",
        _ => "Unknown: Windows' privacy settings couldn't be read.",
    };
}
