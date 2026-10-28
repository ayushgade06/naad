using System.Net.Http.Headers;
using System.Text.Json;

namespace Naad.Transcription;

/// <summary>
/// Sends WAV audio to an OpenAI-compatible /audio/transcriptions endpoint, such as Groq's.
/// Never reads or logs request or response bodies beyond the transcript it returns.
/// </summary>
internal sealed class OpenAiCompatibleTranscriber : ITranscriber
{
    // Long enough for minutes of audio on Groq, short enough not to leave the user waiting.
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly TimeSpan _timeout;

    /// <param name="http">Shared and owned by the caller; one client serves every request.</param>
    /// <param name="apiKey">Sent as a bearer token; never logged.</param>
    /// <param name="baseUrl">The API root, such as https://api.groq.com/openai/v1.</param>
    /// <param name="model">The provider's model id, such as whisper-large-v3-turbo.</param>
    /// <param name="timeout">Tests shorten it; the app uses <see cref="DefaultTimeout"/>.</param>
    /// <exception cref="ArgumentException">A blank key or model, or a base URL that isn't an
    /// absolute http(s) URL. Settings validation keeps these from reaching here.</exception>
    public OpenAiCompatibleTranscriber(
        HttpClient http, string apiKey, Uri baseUrl, string model, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The base URL must be an absolute http or https URL.", nameof(baseUrl));
        }

        _http = http;
        _apiKey = apiKey;
        _model = model;
        _timeout = timeout ?? DefaultTimeout;
        Endpoint = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/audio/transcriptions");
    }

    public Uri Endpoint { get; }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        // HttpClient still hands an already-cancelled request to its handler, which could upload
        // audio after shutdown began.
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = CreateRequest(wav);
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // The body is never read: providers may echo the request in it.
                return new("", TranscriptionFailure.FromStatus(response.StatusCode));
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            return ReadTranscript(await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("", new(TranscriptionError.Timeout));
        }
        catch (HttpRequestException e)
        {
            return new("", new(TranscriptionError.Network, (int)e.HttpRequestError));
        }
        catch (IOException)
        {
            // The connection dropped while the body was being read.
            return new("", new(TranscriptionError.Network));
        }
        catch (JsonException)
        {
            return new("", new(TranscriptionError.MalformedResponse));
        }
    }

    private HttpRequestMessage CreateRequest(byte[] wav)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(_model), "model" },
            { new StringContent("json"), "response_format" },
        };
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "audio.wav");

        // Disposing the request disposes the content and its parts.
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        return request;
    }

    private static TranscriptionResult ReadTranscript(JsonDocument json)
    {
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                return new("", new(TranscriptionError.MalformedResponse));
            }

            // Whisper prefixes its output with a space.
            var transcript = text.GetString()!.Trim();
            if (FillerFilter.IsFiller(transcript))
            {
                Log.Info(LogEvent.TranscriptionFiltered, ("chars", transcript.Length));
                return new("");
            }

            return new(transcript);
        }
    }
}
