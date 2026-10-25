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
}
