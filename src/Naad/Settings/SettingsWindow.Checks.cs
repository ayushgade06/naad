using Naad.Providers;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>The checks Save makes with the service before anything is stored: new keys and custom addresses.</summary>
internal sealed partial class SettingsWindow
{
    // Only a definite rejection stops the save: a service that can't be reached may work later.
    private async Task<string?> RejectsAsync(CloudService service, string key, AppSettings settings)
    {
        var url = service == settings.LanguageModelService
            ? settings.LanguageModelBaseUrl
            : settings.TranscriptionBaseUrl;
        try
        {
            var result = await _actions.TestKey(service, key, url, _closing.Token);
            return result.Failure?.Error == TranscriptionError.Unauthorized
                ? $"{CloudServices.For(service).Name} rejected the new key, so nothing was saved."
                : null;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return "Settings closed before the key was checked.";
        }
    }

    private async Task<string?> EndpointRejectsAsync(CloudChoice endpoint)
    {
        var key = endpoint.NewKey ?? _actions.KeyFor(CloudService.Custom).Key;
        try
        {
            return SettingsInput.EndpointSaveError(
                await _actions.TestKey(CloudService.Custom, key, endpoint.CustomUrl, _closing.Token));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return "Settings closed before the address was checked.";
        }
    }
}
