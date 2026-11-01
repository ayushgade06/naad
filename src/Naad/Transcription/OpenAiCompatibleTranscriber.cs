using System.Net.Http.Headers;
using System.Text.Json;
using Naad.Settings;

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
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly string? _language;
    private readonly string? _prompt;
    private readonly TimeSpan _timeout;

    /// <param name="http">Shared and owned by the caller; one client serves every request.</param>
    /// <param name="apiKey">Sent as a bearer token; never logged.</param>
    /// <param name="baseUrl">The API root, such as https://api.groq.com/openai/v1.</param>
    /// <param name="model">The provider's model id, such as whisper-large-v3-turbo.</param>
    /// <param name="language">An ISO 639-1 code such as "en", or null to let the provider detect it.</param>
    /// <param name="prompt">Names and terms to expect, sent as Whisper's prompt; never logged.</param>
    /// <param name="timeout">Tests shorten it; the app uses <see cref="DefaultTimeout"/>.</param>
    /// <param name="path">The endpoint under the base URL; xAI names it "stt".</param>
    /// <exception cref="ArgumentException">A blank key or model, an invalid language, or a base URL
    /// that isn't an absolute http(s) URL. Settings validation keeps these from reaching here.</exception>
    public OpenAiCompatibleTranscriber(
        HttpClient http,
        string? apiKey,
        Uri baseUrl,
        string model,
        string? language = null,
        string? prompt = null,
        TimeSpan? timeout = null,
        string path = "audio/transcriptions")
    {
        // A server on this PC may need no key; one elsewhere always does.
        if (apiKey is null && !baseUrl.IsLoopback)
        {
            throw new ArgumentException("A key is needed for a server on another machine.", nameof(apiKey));
        }

        // A key that can't go in a header fails every request before it is sent, which would
        // otherwise surface as a vague network error. SecretStore rejects such keys earlier.
        if (apiKey is not null && !SecretStore.IsValidKey(apiKey))
        {
            throw new ArgumentException("The API key holds characters an HTTP header can't carry.", nameof(apiKey));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!baseUrl.IsAbsoluteUri || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The base URL must be an absolute http or https URL.", nameof(baseUrl));
        }

        if (language is not null && !AppSettings.IsValidLanguage(language))
        {
            throw new ArgumentException("The language must be an ISO 639-1 code.", nameof(language));
        }

        _http = http;
        _apiKey = apiKey;
        _model = model;
        _language = language;
        _prompt = prompt;
        _timeout = timeout ?? DefaultTimeout;
        Endpoint = new(baseUrl.AbsoluteUri.TrimEnd('/') + "/" + path);
    }

    public Uri Endpoint { get; }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        // HttpClient still hands an already-cancelled request to its handler, which could upload
        // audio after shutdown began.
        cancellationToken.ThrowIfCancellationRequested();
        // One budget covers every attempt, so a retry never makes the user wait longer.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    await Task.Delay(RetryPolicy.Delay, timeout.Token);
                }

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
                return new("", TranscriptionFailure.ForTimeout(Reachability.IsOnline()));
            }
            catch (HttpRequestException e) when (RetryPolicy.ShouldRetry(e.HttpRequestError, attempt))
            {
                Log.Info(LogEvent.TranscriptionRetried, ("http_error", (int)e.HttpRequestError));
            }
            catch (HttpRequestException e)
            {
                return new("", new(
                    Reachability.IsOnline() ? TranscriptionError.Network : TranscriptionError.NoInternet,
                    (int)e.HttpRequestError));
            }
            catch (IOException)
            {
                // The connection dropped while the body was being read.
                return new("", new(TranscriptionError.ConnectionLost));
            }
            catch (JsonException)
            {
                return new("", new(TranscriptionError.MalformedResponse));
            }
        }
    }

    private HttpRequestMessage CreateRequest(byte[] wav)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(_model), "model" },
            { new StringContent("json"), "response_format" },
        };
        if (_language is not null)
        {
            content.Add(new StringContent(_language), "language");
        }

        if (_prompt is not null)
        {
            content.Add(new StringContent(_prompt), "prompt");
        }

        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "audio.wav");

        // Disposing the request disposes the content and its parts.
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
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
