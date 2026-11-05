namespace Naad.Live;

/// <summary>
/// A few words on why a live stream couldn't be used, for the overlay to say. Pure; never holds
/// the server's reply, which may carry user content.
/// </summary>
internal static class LiveFailure
{
    /// <param name="httpStatus">The status the server answered the connection with; 0 or 101 when none or fine.</param>
    /// <param name="connected">Whether the stream ever opened.</param>
    /// <param name="dropped">Whether it broke off after opening.</param>
    /// <param name="serverError">Whether the server reported an error.</param>
    public static string Describe(int httpStatus, bool connected, bool dropped, bool serverError) => httpStatus switch
    {
        401 or 403 => "key refused",
        >= 400 => $"HTTP {httpStatus}",
        _ when !connected => "no connection",
        _ when dropped => "connection lost",
        _ when serverError => "service error",
        _ => "no reply",
    };
}
