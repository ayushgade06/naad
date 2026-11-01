using Naad.Providers;
using Naad.Settings;

namespace Naad.Live;

/// <summary>Where a stream goes and what it carries, as the settings choose it.</summary>
/// <param name="Endpoint">The WebSocket address.</param>
/// <param name="Key">The service's key; null for a custom server that needs none. Never logged.</param>
/// <param name="Model">The model; empty for a custom server's own.</param>
internal sealed record LiveStream(Uri Endpoint, string? Key, string Model)
{
    // The generated ToString would print the key.
    public override string ToString() => $"LiveStream {{ Host = {Endpoint.Host}, HasKey = {Key is not null} }}";
}

/// <summary>Which stream, if any, the live settings make. Pure: decided from the settings and keys.</summary>
internal static class LiveSetup
{
    /// <summary>The cloud service whose key a live service uses; null for this PC.</summary>
    public static CloudService? ServiceFor(LiveService live) => live switch
    {
        LiveService.OpenAI => CloudService.OpenAI,
        LiveService.Deepgram => CloudService.Deepgram,
        LiveService.Gemini => CloudService.Gemini,
        LiveService.XAI => CloudService.XAI,
        LiveService.Custom => CloudService.Custom,
        _ => null,
    };

    /// <summary>
    /// The stream <paramref name="s"/> chooses, or null when there is none to make: live runs on
    /// this PC, a named service has no key, or the custom base URL isn't a realtime address. A
    /// custom server on this PC may need no key.
    /// </summary>
    public static LiveStream? Stream(AppSettings s, Func<CloudService, string?> keyFor)
    {
        if (ServiceFor(s.LiveService) is not { } service)
        {
            return null;
        }

        var key = keyFor(service);
        if (key is null && s.LiveService != LiveService.Custom)
        {
            return null;
        }

        var model = s.LiveModel ?? RealtimeProtocol.DefaultModel(s.LiveService);
        var baseUrl = s.LiveService == LiveService.Custom ? s.TranscriptionBaseUrl : CloudServices.For(service).BaseUrl;
        return RealtimeProtocol.Endpoint(s.LiveService, model, s.Language, baseUrl) is { } endpoint
            ? new(endpoint, key, model)
            : null;
    }
}
