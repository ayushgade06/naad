namespace Naad.Transcription;

/// <summary>Where speech becomes text.</summary>
internal enum TranscriptionProvider
{
    /// <summary>An OpenAI-compatible API such as Groq's; needs an API key and the network.</summary>
    Cloud,

    /// <summary>Whisper on this PC; needs the downloaded model, never the network.</summary>
    Local,
}

/// <summary>Which transcriber can run now. Pure: it decides from what is in place.</summary>
internal static class ProviderSelection
{
    /// <summary>
    /// The chosen provider if it can run: the cloud needs a key, the local one a downloaded
    /// model. Null otherwise, so the shortcut asks the user to finish setting up instead of
    /// recording words that can't be transcribed. Never falls back to the other provider: a user
    /// who chose local transcription must never have audio uploaded behind their back.
    /// </summary>
    public static TranscriptionProvider? Choose(TranscriptionProvider chosen, bool hasKey, bool modelReady) =>
        chosen switch
        {
            TranscriptionProvider.Cloud when hasKey => TranscriptionProvider.Cloud,
            TranscriptionProvider.Local when modelReady => TranscriptionProvider.Local,
            _ => null,
        };
}
