using System.Net.Http.Headers;
using Naad.Settings;
using Naad.Transcription;

namespace Naad.Providers;

/// <summary>
/// Asks a service whether a key works, with a request that needs the key, uploads nothing and
/// changes nothing: listing its models (Deepgram: its projects). Only the status is read, never
/// the body. Expected failures come back as values.
/// </summary>
internal static class ServiceKeyCheck
{
    // A tiny request; any service answers it within a few seconds, and the user is watching.
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // Anthropic's own endpoints need an API version header.
    private const string AnthropicVersion = "2023-06-01";

    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="service">Whose key it is.</param>
    /// <param name="key">The key; null only for a custom server on this PC that needs none.</param>
    /// <param name="customBaseUrl">The endpoint, for <see cref="CloudService.Custom"/>.</param>
    /// <param name="cancellationToken">Cancelled when Settings closes.</param>
    /// <param name="timeout">Tests shorten it.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ArgumentException">The key isn't <see cref="SecretStore.IsValidKey"/>.</exception>
    public static async Task<KeyTestResult> TestAsync(
        HttpClient http,
        CloudService service,
        string? key,
        Uri customBaseUrl,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (key is not null && !SecretStore.IsValidKey(key))
        {
            throw new ArgumentException("Not a valid API key.", nameof(key));
        }

        var preset = CloudServices.For(service);
        var url = preset.KeyCheck ?? new Uri(customBaseUrl.AbsoluteUri.TrimEnd('/') + "/models");
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeout ?? Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (key is not null)
            {
                AddKey(request, preset.KeyCheckHeader, key);
            }

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
            return (int)response.StatusCode switch
            {
                >= 200 and < 300 => new(KeyTestOutcome.Accepted),

                // Not every OpenAI-compatible server lists models; that says nothing about the key.
                404 or 405 => new(KeyTestOutcome.CannotVerify),
                _ => new(KeyTestOutcome.Failed, TranscriptionFailure.FromStatus(response.StatusCode)),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(KeyTestOutcome.Failed, TranscriptionFailure.ForTimeout(Reachability.IsOnline()));
        }
        catch (HttpRequestException e)
        {
            return new(KeyTestOutcome.Failed, new(
                Reachability.IsOnline() ? TranscriptionError.Network : TranscriptionError.NoInternet,
                (int)e.HttpRequestError));
        }
    }

    private static void AddKey(HttpRequestMessage request, KeyHeader header, string key)
    {
        switch (header)
        {
            case KeyHeader.Token:
                request.Headers.Authorization = new AuthenticationHeaderValue("Token", key);
                break;
            case KeyHeader.ApiKey:
                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", AnthropicVersion);
                break;
            case KeyHeader.GoogleApiKey:
                request.Headers.Add("x-goog-api-key", key);
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                break;
        }
    }
}
