using System.Net;

namespace Naad.Transcription;

internal enum TranscriptionError
{
    /// <summary>401 or 403: the API key is wrong, revoked, or not allowed to use the model.</summary>
    Unauthorized,

    /// <summary>413: the audio is larger than the provider accepts.</summary>
    TooLarge,

    /// <summary>429: rate limit or quota reached.</summary>
    RateLimited,

    /// <summary>5xx: the provider failed.</summary>
    ServerError,

    /// <summary>Any other 4xx, such as an unknown model or a wrong base URL (404).</summary>
    Rejected,

    /// <summary>No HTTP response: DNS, connection or TLS failed with a network connected.</summary>
    Network,

    /// <summary>No HTTP response, and the PC has no network connection.</summary>
    NoInternet,

    /// <summary>The connection dropped while the answer was arriving.</summary>
    ConnectionLost,

    /// <summary>No response within the request timeout.</summary>
    Timeout,

    /// <summary>A success status whose body isn't the expected JSON.</summary>
    MalformedResponse,

    /// <summary>Transcribing on this PC failed: the model is missing or damaged, or whisper.cpp failed.</summary>
    LocalFailed,
}

/// <summary>Why a transcription failed. Caller cancellation is not a failure; it throws.</summary>
/// <param name="Error">The kind of failure, which decides what the user is told.</param>
/// <param name="Code">For the log: the HTTP status, or for Network the <see cref="HttpRequestError"/>;
/// otherwise 0.</param>
internal readonly record struct TranscriptionFailure(TranscriptionError Error, int Code = 0)
{
    /// <summary>
    /// A request that got no answer in time. Offline, it was the connection, not the service: a
    /// request hung by Wi-Fi dropping looks just like a slow server.
    /// </summary>
    public static TranscriptionFailure ForTimeout(bool online) =>
        new(online ? TranscriptionError.Timeout : TranscriptionError.NoInternet);

    /// <summary>Maps a non-success HTTP status to a failure.</summary>
    public static TranscriptionFailure FromStatus(HttpStatusCode status) => new(
        (int)status switch
        {
            401 or 403 => TranscriptionError.Unauthorized,
            413 => TranscriptionError.TooLarge,
            429 => TranscriptionError.RateLimited,
            >= 500 => TranscriptionError.ServerError,
            _ => TranscriptionError.Rejected,
        },
        (int)status);
}

/// <param name="Text">The transcript, trimmed; empty when nothing was said. Empty on failure.</param>
/// <param name="Failure">Why it failed, or null on success.</param>
internal readonly record struct TranscriptionResult(string Text, TranscriptionFailure? Failure = null)
{
    // The generated ToString would print the transcript into any log or exception that used it.
    public override string ToString() =>
        $"TranscriptionResult {{ Chars = {Text.Length}, Failure = {Failure?.ToString() ?? "none"} }}";
}
