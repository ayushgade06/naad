namespace Naad.Transcription;

internal enum KeyTestOutcome
{
    /// <summary>The provider accepted the key.</summary>
    Accepted,

    /// <summary>The provider can't list models (404 or 405), so the key couldn't be checked.</summary>
    CannotVerify,

    /// <summary>The check failed; <see cref="KeyTestResult.Failure"/> says why.</summary>
    Failed,
}

/// <param name="Outcome">What the check found.</param>
/// <param name="Failure">Why it failed, when <paramref name="Outcome"/> is Failed; otherwise null.</param>
internal readonly record struct KeyTestResult(KeyTestOutcome Outcome, TranscriptionFailure? Failure = null);
