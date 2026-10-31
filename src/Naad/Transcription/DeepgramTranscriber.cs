using System.Net.Http.Headers;
using System.Text.Json;
using Naad.Cleanup;
using Naad.Settings;

namespace Naad.Transcription;

/// <summary>
/// Sends WAV audio to Deepgram's pre-recorded API (<c>POST /v1/listen</c>), which isn't
/// OpenAI-compatible: the audio is the request body, options go in the query string, and the key
/// goes in an <c>Authorization: Token</c> header. Never logs the transcript or the response.
/// </summary>
internal sealed class DeepgramTranscriber : ITranscriber
{
    private static readonly Uri Listen = new("https://api.deepgram.com/v1/listen");

    // Deepgram's keyterm prompting takes up to 100 terms.
    private const int MaxKeyterms = 100;

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;

    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="apiKey">Sent as a token; never logged.</param>
    /// <param name="model">Such as nova-3.</param>
    /// <param name="language">An ISO 639-1 code, or null to detect the language.</param>
    /// <param name="vocabulary">Names and terms to expect, sent as key terms; never logged.</param>
    /// <param name="timeout">How long a request may take.</param>
    /// <exception cref="ArgumentException">An invalid key or model; settings validation stops these
    /// earlier.</exception>
    public DeepgramTranscriber(
        HttpClient http, string apiKey, string model, string? language, string? vocabulary, TimeSpan timeout)
    {
        if (!SecretStore.IsValidKey(apiKey))
        {
            throw new ArgumentException("Not a valid API key.", nameof(apiKey));
        }

        if (!AppSettings.IsValidModel(model))
        {
            throw new ArgumentException("Not a valid model id.", nameof(model));
        }

        _http = http;
        _apiKey = apiKey;
        _timeout = timeout;
        _endpoint = Endpoint(model, language, vocabulary);
    }

    /// <summary>The request URL with its options. Public for tests.</summary>
    public static Uri Endpoint(string model, string? language, string? vocabulary)
    {
        var query = new List<string> { "model=" + Uri.EscapeDataString(model), "smart_format=true" };
        query.Add(language is null ? "detect_language=true" : "language=" + Uri.EscapeDataString(language));
        foreach (var term in CleanupPrompt.Terms(vocabulary).Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxKeyterms))
        {
            query.Add("keyterm=" + Uri.EscapeDataString(term));
        }

        return new(Listen + "?" + string.Join('&', query));
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new ByteArrayContent(wav),
            };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", _apiKey);
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new("", TranscriptionFailure.FromStatus(response.StatusCode));
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return Read(json.RootElement);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("", new(TranscriptionError.Timeout));
        }
        catch (HttpRequestException e)
        {
            return new("", new(
                Reachability.IsOnline() ? TranscriptionError.Network : TranscriptionError.NoInternet,
                (int)e.HttpRequestError));
        }
        catch (IOException)
        {
            return new("", new(TranscriptionError.ConnectionLost));
        }
        catch (JsonException)
        {
            return new("", new(TranscriptionError.MalformedResponse));
        }
    }

    // results.channels[0].alternatives[0].transcript
    private static TranscriptionResult Read(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Object
            && results.TryGetProperty("channels", out var channels) && channels.ValueKind == JsonValueKind.Array
            && channels.GetArrayLength() > 0 && channels[0].ValueKind == JsonValueKind.Object
            && channels[0].TryGetProperty("alternatives", out var alternatives)
            && alternatives.ValueKind == JsonValueKind.Array && alternatives.GetArrayLength() > 0
            && alternatives[0].ValueKind == JsonValueKind.Object
            && alternatives[0].TryGetProperty("transcript", out var transcript)
            && transcript.ValueKind == JsonValueKind.String)
        {
            return new(transcript.GetString()!.Trim());
        }

        return new("", new(TranscriptionError.MalformedResponse));
    }
}
