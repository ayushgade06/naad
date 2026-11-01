using Naad.Providers;
using Naad.Settings;

namespace Naad.Transcription;

/// <summary>Builds the transcriber the settings choose.</summary>
internal static class TranscriberSetup
{
    /// <summary>
    /// The transcriber for <paramref name="s"/>, or null when the chosen one can't run yet: the
    /// cloud without a key (a custom server on this PC may need none), or this PC without the
    /// model. A cloud transcriber falls back to this PC when offline, if its model is downloaded.
    /// </summary>
    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="keyFor">A service's API key, or null.</param>
    /// <param name="s">The settings.</param>
    /// <param name="offline">Called when a dictation was transcribed on this PC because the PC was offline.</param>
    public static ITranscriber? Create(
        HttpClient http, Func<CloudService, string?> keyFor, AppSettings s, Action offline)
    {
        var model = LocalModel.For(s.LocalModel);
        var service = s.TranscriptionService;
        var key = keyFor(service);
        var keyless = service == CloudService.Custom && s.TranscriptionBaseUrl.IsLoopback;
        return ProviderSelection.Choose(s.TranscriptionProvider, key is not null || keyless, model.IsReady()) switch
        {
            TranscriptionProvider.Cloud when model.IsReady() => new OfflineFallbackTranscriber(
                Cloud(http, service, key, s),
                Local(model, s),
                Reachability.IsOnline,
                offline),
            TranscriptionProvider.Cloud => Cloud(http, service, key, s),
            TranscriptionProvider.Local => new LocalWhisperTranscriber(model.Path, s.Language, s.TranscriptionPrompt),
            _ => null,
        };
    }

    /// <summary>
    /// The transcriber <paramref name="s"/> chooses, without the offline fallback, for Test: it
    /// must check the chosen service, never quietly pass on this PC. Null when it can't run.
    /// </summary>
    public static ITranscriber? CreateForTest(HttpClient http, string? key, AppSettings s)
    {
        var model = LocalModel.For(s.LocalModel);
        var keyless = s.TranscriptionService == CloudService.Custom && s.TranscriptionBaseUrl.IsLoopback;
        return ProviderSelection.Choose(s.TranscriptionProvider, key is not null || keyless, model.IsReady()) switch
        {
            TranscriptionProvider.Cloud => Cloud(http, s.TranscriptionService, key, s),
            TranscriptionProvider.Local => new LocalWhisperTranscriber(model.Path, s.Language, s.TranscriptionPrompt),
            _ => null,
        };
    }

    private static ITranscriber Cloud(HttpClient http, CloudService service, string? key, AppSettings s)
    {
        var timeout = TimeSpan.FromSeconds(s.TranscriptionTimeoutSeconds);
        var url = CloudServices.For(service).BaseUrl ?? s.TranscriptionBaseUrl;
        return service switch
        {
            CloudService.Deepgram => new DeepgramTranscriber(
                http, key!, s.TranscriptionModel, s.Language, s.TranscriptionPrompt, timeout),
            CloudService.Gemini => new GeminiTranscriber(
                http, key!, s.TranscriptionModel, s.Language, s.TranscriptionPrompt, timeout),

            // xAI's speech-to-text takes the OpenAI form at its own path.
            CloudService.XAI => new OpenAiCompatibleTranscriber(
                http, key, url, s.TranscriptionModel, s.Language, s.TranscriptionPrompt, timeout, path: "stt"),
            _ => new OpenAiCompatibleTranscriber(
                http, key, url, s.TranscriptionModel, s.Language, s.TranscriptionPrompt, timeout),
        };
    }

    private static Func<ITranscriber?> Local(LocalModel model, AppSettings s) =>
        () => model.IsReady() ? new LocalWhisperTranscriber(model.Path, s.Language, s.TranscriptionPrompt) : null;
}
