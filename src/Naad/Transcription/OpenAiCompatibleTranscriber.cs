using System.Net.Http.Headers;
using System.Text.Json;

namespace Naad.Transcription;

/// <summary>Sends WAV audio to an OpenAI-compatible /audio/transcriptions endpoint.</summary>
internal sealed class OpenAiCompatibleTranscriber(HttpClient http, string apiKey)
{
    // ponytail: Groq and the model are hard-coded and failures surface as exceptions;
    // P13 makes them configurable and maps failures to typed results.
    private const string Model = "whisper-large-v3-turbo";
    private static readonly Uri Endpoint = new("https://api.groq.com/openai/v1/audio/transcriptions");

    // Long enough for minutes of audio on Groq, short enough not to leave the user waiting.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <exception cref="HttpRequestException">Network failure or a non-success status.</exception>
    /// <exception cref="OperationCanceledException">The request timed out.</exception>
    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(Model), "model");
        content.Add(new StringContent("json"), "response_format");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "audio.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, timeout.Token);

        // The exception carries the status code only; the body may echo user content.
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var json = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);

        // Whisper prefixes its output with a space.
        return json.RootElement.GetProperty("text").GetString()?.Trim() ?? "";
    }
}
