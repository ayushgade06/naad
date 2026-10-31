using System.Net.NetworkInformation;

namespace Naad.Transcription;

/// <summary>Whether the PC has a network connection right now.</summary>
internal static class Reachability
{
    /// <summary>
    /// False when no network adapter other than loopback is connected: Wi-Fi off, cable out,
    /// airplane mode. True doesn't promise the internet works, as on a captive portal; the request
    /// itself finds that out.
    /// </summary>
    public static bool IsOnline() => NetworkInterface.GetIsNetworkAvailable();
}
