namespace Naad.LanguageModel;

/// <summary>Where AI steps such as cleanup run.</summary>
internal enum LanguageModelProvider
{
    /// <summary>A model on this PC; needs the downloaded model, never the network.</summary>
    Local,

    /// <summary>An OpenAI-compatible chat API; needs the network, and a key unless it runs on this PC.</summary>
    Cloud,
}

/// <summary>Which language model can run now. Pure: it decides from what is in place.</summary>
internal static class LanguageModelSelection
{
    /// <summary>
    /// The chosen provider if it can run: the cloud needs a key (a server on this PC may need
    /// none), the local one a downloaded model. Null otherwise. Never falls back to the other
    /// provider: text the user chose to keep on this PC must never be uploaded behind their back.
    /// </summary>
    public static LanguageModelProvider? Choose(
        LanguageModelProvider chosen, bool hasKey, bool loopbackUrl, bool modelReady) => chosen switch
        {
            LanguageModelProvider.Cloud when hasKey || loopbackUrl => LanguageModelProvider.Cloud,
            LanguageModelProvider.Local when modelReady => LanguageModelProvider.Local,
            _ => null,
        };
}
