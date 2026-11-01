using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Naad.Cleanup;
using Naad.Settings;

namespace Naad.Transcription;

/// <summary>
/// Transcribes with Gemini's own API (<c>models/{model}:generateContent</c>), whose
/// OpenAI-compatible endpoint has no audio transcription: the WAV goes inline, base64-encoded,
/// with an instruction to write down what was said. The key goes in <c>x-goog-api-key</c>. Never
/// logs the transcript or the response.
/// </summary>
internal sealed class GeminiTranscriber : ITranscriber
{
    /// <summary>
    /// Gemini refuses inline requests over 20 MB; base64 grows the audio by a third, and 14 MB of
    /// WAV is over seven minutes, past Naad's five-minute recording limit anyway.
    /// </summary>
    public const int MaxWavBytes = 14_000_000;

    private const string Instructions = "Transcribe the speech in this audio exactly as spoken, with punctuation. " +
        "Reply with the transcript only. If there is no speech, reply with nothing.";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string? _language;
    private readonly string? _vocabulary;
    private readonly TimeSpan _timeout;

    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="apiKey">Sent in x-goog-api-key; never logged.</param>
    /// <param name="model">Such as gemini-2.5-flash.</param>
    /// <param name="language">An ISO 639-1 code, or null to detect the language.</param>
    /// <param name="vocabulary">Names and terms to expect; never logged.</param>
    /// <param name="timeout">How long a request may take.</param>
    /// <exception cref="ArgumentException">An invalid key or model.</exception>
    public GeminiTranscriber(
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
        _endpoint = new(
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent");
        _language = language;
        _vocabulary = vocabulary;
        _timeout = timeout;
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wav.Length > MaxWavBytes)
        {
            return new("", new(TranscriptionError.TooLarge));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(Body(wav)),
            };
            request.Headers.Add("x-goog-api-key", _apiKey);
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
            return new("", TranscriptionFailure.ForTimeout(Reachability.IsOnline()));
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

    private JsonObject Body(byte[] wav)
    {
        var instructions = new StringBuilder(Instructions);
        if (_language is not null)
        {
            instructions.Append(" The speech is in the language with ISO 639-1 code \"" + _language + "\".");
        }

        if (CleanupPrompt.Terms(_vocabulary) is { Length: > 0 } terms)
        {
            instructions.Append(" Names and terms that may come up: " + terms + ".");
        }

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["parts"] = new JsonArray(
                    new JsonObject { ["text"] = instructions.ToString() },
                    new JsonObject
                    {
                        ["inline_data"] = new JsonObject
                        {
                            ["mime_type"] = "audio/wav",
                            ["data"] = Convert.ToBase64String(wav),
                        },
                    }),
            }),

            // Zero: a transcript has one right answer.
            ["generationConfig"] = new JsonObject { ["temperature"] = 0 },
        };
    }

    // candidates[0].content.parts[*].text, joined.
    private static TranscriptionResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return new("", new(TranscriptionError.MalformedResponse));
        }

        // No candidate: Gemini found nothing to say, as for silence.
        if (candidates.GetArrayLength() == 0 || candidates[0].ValueKind != JsonValueKind.Object
            || !candidates[0].TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return new("");
        }

        var text = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var piece)
                && piece.ValueKind == JsonValueKind.String)
            {
                text.Append(piece.GetString());
            }
        }

        return new(text.ToString().Trim());
    }
}
