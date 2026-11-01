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
        var withTemperature = !ModelOptions.OmitsTemperature(_model) && !_temperature.IsRefused(_baseUrl, _model);
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

                    if ((int)response.StatusCode == 429)
                    {
                        var (wait, daily) = RateLimitHeaders.Read(response.Headers);
                        return new("", new(CompletionError.RateLimited, 429, wait, daily));
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
                new JsonObject { ["role"] = "user", ["content"] = UserContent(prompt) }),
            [ModelOptions.UsesMaxCompletionTokens(_model) ? "max_completion_tokens" : "max_tokens"] =
                ModelOptions.MaxTokens(_model, prompt.MaxTokens),
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

        if (ModelOptions.SendsIncludeReasoning(_baseUrl, _model))
        {
            body["include_reasoning"] = false;
        }

        // Disposing the request disposes the content.
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return request;
    }

    // Text alone, or with an image in OpenAI's content-parts form, which vision models read.
    private static JsonNode UserContent(ChatPrompt prompt) => prompt.Image is not { } png
        ? JsonValue.Create(prompt.Text)
        : new JsonArray(
            new JsonObject { ["type"] = "text", ["text"] = prompt.Text },
            new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) },
            });

    // OpenAI's shape: { "error": { "param": "temperature", ... } }; Anthropic's has no param, only
    // { "type": "error", "error": { "type": "invalid_request_error", "message": "temperature: ..." } }.
    // The message may quote the request: it is only searched for the word, never kept or logged.
    private static async Task<bool> RefusesTemperatureAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(body, cancellationToken: token);
            if (json.RootElement.ValueKind != JsonValueKind.Object
                || !json.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (error.TryGetProperty("param", out var param) && param.ValueKind == JsonValueKind.String)
            {
                return param.GetString() == "temperature";
            }

            return error.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
                && type.GetString() == "invalid_request_error"
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && message.GetString()!.Contains("temperature", StringComparison.OrdinalIgnoreCase);
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
