using System.Net;

namespace Naad.LanguageModel;

internal enum CompletionError
{
    /// <summary>401 or 403: the API key is wrong, revoked, or not allowed to use the model.</summary>
    Unauthorized,

    /// <summary>429: rate limit or quota reached.</summary>
    RateLimited,

    /// <summary>5xx: the provider failed.</summary>
    ServerError,

    /// <summary>Any other 4xx, such as an unknown model or a wrong base URL (404).</summary>
    Rejected,

    /// <summary>No HTTP response: DNS, connection, TLS or a dropped connection.</summary>
    Network,

    /// <summary>No reply within the request timeout.</summary>
    Timeout,

    /// <summary>A success status whose body isn't the expected JSON.</summary>
    MalformedResponse,

    /// <summary>The model on this PC is missing or damaged, or llama.cpp failed.</summary>
    LocalFailed,
}

/// <summary>Why a completion failed. Caller cancellation is not a failure; it throws.</summary>
/// <param name="Error">The kind of failure.</param>
/// <param name="Code">For the log: the HTTP status, or for Network the <see cref="HttpRequestError"/>;
/// otherwise 0.</param>
internal readonly record struct CompletionFailure(CompletionError Error, int Code = 0)
{
    /// <summary>Maps a non-success HTTP status to a failure.</summary>
    public static CompletionFailure FromStatus(HttpStatusCode status) => new(
        (int)status switch
        {
            401 or 403 => CompletionError.Unauthorized,
            429 => CompletionError.RateLimited,
            >= 500 => CompletionError.ServerError,
            _ => CompletionError.Rejected,
        },
        (int)status);
}

/// <param name="Text">The reply, trimmed and without reasoning blocks; empty on failure.</param>
/// <param name="Failure">Why it failed, or null on success.</param>
internal readonly record struct CompletionResult(string Text, CompletionFailure? Failure = null)
{
    // The generated ToString would print the reply, which is user content, into any log.
    public override string ToString() =>
        $"CompletionResult {{ Chars = {Text.Length}, Failure = {Failure?.ToString() ?? "none"} }}";
}
