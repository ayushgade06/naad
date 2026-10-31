using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Naad.Settings;
using Naad.Transcription;

namespace Naad.LanguageModel;

/// <summary>
/// Sends a prompt to an OpenAI-compatible <c>/chat/completions</c> endpoint: Groq, OpenAI,
/// OpenRouter, or a server on this PC such as Ollama or LM Studio. Never logs request or response
/// bodies, and reads an error body only for the field that says a temperature was refused.
/// </summary>
internal sealed class OpenAiChatLanguageModel : ILanguageModel
{
    /// <summary>Long enough for a slow free tier to clean a minute of dictation.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly Uri _baseUrl;
    private readonly string _model;
    private readonly TemperatureSupport _temperature;
    private readonly TimeSpan _timeout;

    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="apiKey">Sent as a bearer token; never logged. Null for a server on this PC
    /// that needs none.</param>
    /// <param name="baseUrl">The API root, such as https://api.groq.com/openai/v1.</param>
    /// <param name="model">The provider's model id.</param>
    /// <param name="temperature">Which models refused a temperature.</param>
    /// <param name="timeout">Tests shorten it; the app uses <see cref="DefaultTimeout"/>.</param>
    /// <exception cref="ArgumentException">An invalid key, model or base URL. Settings validation
    /// keeps these from reaching here.</exception>
    public OpenAiChatLanguageModel(
        HttpClient http,
        string? apiKey,
        Uri baseUrl,
        string model,
        TemperatureSupport temperature,
        TimeSpan? timeout = null)
    {
        if (apiKey is not null && !SecretStore.IsValidKey(apiKey))
        {
            throw new ArgumentException("The API key holds characters an HTTP header can't carry.", nameof(apiKey));
        }

        if (!AppSettings.IsValidModel(model))
        {
            throw new ArgumentException("Not a valid model id.", nameof(model));
        }

        if (!AppSettings.TryParseBaseUrl(baseUrl.AbsoluteUri, out _))
        {
            throw new ArgumentException("The base URL must be https, or http on this PC.", nameof(baseUrl));
        }

        _http = http;
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _model = model;
        _temperature = temperature;
        _timeout = timeout ?? DefaultTimeout;
        Endpoint = new(baseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions");
    }

    public Uri Endpoint { get; }

    public async Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
    {
        // HttpClient still hands an already-cancelled request to its handler.
        cancellationToken.ThrowIfCancellationRequested();

        // One budget covers every attempt, so a retry never makes the user wait longer.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        var withTemperature = !_temperature.IsRefused(_baseUrl, _model);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (attempt > 1)
                {
                    await Task.Delay(RetryPolicy.Delay, timeout.Token);
                }

                using var request = CreateRequest(prompt, withTemperature);
                using var response = await _http.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    // Sent again at once without the temperature; that isn't a retry of a failure.
                    if (withTemperature && (int)response.StatusCode == 400
                        && await RefusesTemperatureAsync(response, timeout.Token))
                    {
                        _temperature.MarkRefused(_baseUrl, _model);
                        Log.Info(LogEvent.LanguageModelTemperatureRefused);
                        withTemperature = false;
                        attempt = 0;
                        continue;
                    }

                    return new("", CompletionFailure.FromStatus(response.StatusCode));
                }

                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                return ReadReply(await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new("", new(CompletionError.Timeout));
            }
            catch (HttpRequestException e) when (RetryPolicy.ShouldRetry(e.HttpRequestError, attempt))
            {
                Log.Info(LogEvent.LanguageModelRetried, ("http_error", (int)e.HttpRequestError));
            }
            catch (HttpRequestException e)
            {
                return new("", new(CompletionError.Network, (int)e.HttpRequestError));
            }
            catch (IOException)
            {
                // The connection dropped while the body was being read.
                return new("", new(CompletionError.Network));
            }
            catch (JsonException)
            {
                return new("", new(CompletionError.MalformedResponse));
            }
        }
    }

    private HttpRequestMessage CreateRequest(ChatPrompt prompt, bool withTemperature)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = prompt.Instructions },
                new JsonObject { ["role"] = "user", ["content"] = prompt.Text }),
            [ModelOptions.UsesMaxCompletionTokens(_model) ? "max_completion_tokens" : "max_tokens"] =
                prompt.MaxTokens,
            ["stream"] = false,
        };

        // Zero: cleanup wants the most likely wording, not variety.
        if (withTemperature)
        {
            body["temperature"] = 0;
        }

        if (ModelOptions.ReasoningEffort(_model) is { } effort)
        {
            body["reasoning_effort"] = effort;
        }

        // Disposing the request disposes the content.
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return request;
    }

    // OpenAI's shape: { "error": { "param": "temperature", ... } }. Only that field is read; the
    // message may quote the request.
    private static async Task<bool> RefusesTemperatureAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: token);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("param", out var param)
                && param.ValueKind == JsonValueKind.String
                && param.GetString() == "temperature";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CompletionResult ReadReply(JsonDocument json)
    {
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || choices[0].ValueKind != JsonValueKind.Object
                || !choices[0].TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.String)
            {
                return new("", new(CompletionError.MalformedResponse));
            }

            return new(ReplyText.Clean(content.GetString()!));
        }
    }
}
