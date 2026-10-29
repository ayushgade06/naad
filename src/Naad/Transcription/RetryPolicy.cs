namespace Naad.Transcription;

/// <summary>
/// When a failed transcription request is sent again. Pure: it decides from the failure alone.
/// </summary>
internal static class RetryPolicy
{
    /// <summary>
    /// One retry: enough to ride out a Wi-Fi reconnect or a dropped first connection, without
    /// doubling a long wait for a service that is really down.
    /// </summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// Long enough for a reconnecting network adapter or DNS to settle, short next to the
    /// 20-second budget that covers both attempts.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to try again after <paramref name="attemptsMade"/> attempts ended in
    /// <paramref name="error"/>. Only failures where the request never reached the server count,
    /// so a retry can never transcribe, or bill, the same audio twice. Timeouts, dropped responses
    /// and HTTP errors are never retried.
    /// </summary>
    public static bool ShouldRetry(HttpRequestError error, int attemptsMade) =>
        attemptsMade < MaxAttempts
        && error is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError;
}
