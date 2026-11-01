using Naad.LanguageModel;

namespace Naad.Context;

/// <summary>Why the context model gave no summary. Holds no user content, so it may be logged.</summary>
internal enum ContextFailure
{
    /// <summary>The service rejected the key.</summary>
    Key,

    /// <summary>The service doesn't know the context model.</summary>
    ModelNotFound,

    /// <summary>The service's rate limit was reached.</summary>
    RateLimited,

    /// <summary>No summary in time.</summary>
    Timeout,

    /// <summary>The service couldn't be reached.</summary>
    Network,

    /// <summary>The service had a problem.</summary>
    Server,

    /// <summary>The request was refused, the reply unreadable or empty, or the model on this PC failed.</summary>
    Unusable,
}

/// <summary>What a failed summary means for the user, in one sentence. Pure.</summary>
internal static class ContextFailures
{
    /// <summary>The reason for <paramref name="failure"/>; a reply with no words is unusable.</summary>
    public static ContextFailure From(CompletionFailure failure) => failure.Error switch
    {
        CompletionError.Unauthorized => ContextFailure.Key,
        CompletionError.Rejected when failure.Code == 404 => ContextFailure.ModelNotFound,
        CompletionError.RateLimited => ContextFailure.RateLimited,
        CompletionError.Timeout => ContextFailure.Timeout,
        CompletionError.Network => ContextFailure.Network,
        CompletionError.ServerError => ContextFailure.Server,
        _ => ContextFailure.Unusable,
    };

    /// <summary>Tells the user why, and what to try. Windows cuts notification text at 255 characters.</summary>
    public static string Message(ContextFailure failure) => "The context summary failed: " + failure switch
    {
        ContextFailure.Key => "the service rejected the key. Check it under AI in Settings.",
        ContextFailure.ModelNotFound => "the service doesn't know the context model. Choose another under AI.",
        ContextFailure.RateLimited => "the service's rate limit was reached. It works again once the limit resets.",
        ContextFailure.Timeout => "it took too long. A faster context model may help.",
        ContextFailure.Network => "the service couldn't be reached.",
        ContextFailure.Server => "the service had a problem. Try again later.",
        _ => "the reply couldn't be used. Try again or choose another context model.",
    } + " Dictation goes on without it.";
}
