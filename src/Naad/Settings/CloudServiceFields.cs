using Naad.Providers;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>
/// The cloud fields shared by the Dictation and AI tabs: the service, a base URL for a custom
/// endpoint, the model (and on the AI tab a fallback model) with suggestions and Reset, and the
/// service's key, write-only, with Test key. Keys are per service, so a key typed on one tab
/// serves the other too. Create and use it on the UI thread.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA2213:Disposable fields should be disposed",
    Justification = "AddRows puts every control on the page's table, which disposes them.")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "AddRows puts every control on the page's table, which disposes them.")]
internal sealed class CloudServiceFields
{
    private const int FieldWidth = 300;

    private readonly IReadOnlyList<CloudService> _services;
    private readonly bool _transcription;
    private readonly Func<CloudService, ApiKeyResult> _keyFor;
    private readonly Func<CloudService, string?, Uri, CancellationToken, Task<KeyTestResult>> _testKey;
    private readonly CancellationToken _closing;
    private readonly Uri _customUrl;

    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly TextBox _baseUrl = new() { Width = FieldWidth };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = FieldWidth };
    private readonly ComboBox? _fallback;
    private readonly Label _keyStatus = Note("");
    private readonly TextBox _newKey = new() { UseSystemPasswordChar = true, Width = FieldWidth };
    private readonly Button _testKeyButton = new() { Text = "Test key", AutoSize = true };
    private readonly Label _testResult = Note("");

    /// <param name="services">The services this tab offers.</param>
    /// <param name="current">The chosen service.</param>
    /// <param name="customUrl">The custom endpoint in settings.</param>
    /// <param name="model">The chosen model.</param>
    /// <param name="fallback">The fallback model, or null for none; ignored without
    /// <paramref name="withFallback"/>.</param>
    /// <param name="withFallback">Whether to show a fallback model field (the AI tab).</param>
    /// <param name="keyFor">Where a service's key comes from.</param>
    /// <param name="testKey">Checks a key (null for none) for a service and custom endpoint.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public CloudServiceFields(
        IReadOnlyList<CloudService> services,
        CloudService current,
        Uri customUrl,
        string model,
        string? fallback,
        bool withFallback,
        Func<CloudService, ApiKeyResult> keyFor,
        Func<CloudService, string?, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        CancellationToken closing)
    {
        _services = services;
        _transcription = !withFallback;
        _keyFor = keyFor;
        _testKey = testKey;
        _closing = closing;
        _customUrl = customUrl;
        _service.Items.AddRange([.. services.Select(s => CloudServices.For(s).Name)]);
        _service.SelectedIndex = Math.Max(0, IndexOf(services, current));
        if (withFallback)
        {
            _fallback = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = FieldWidth };
        }

        ShowService();
        _model.Text = model;
        _fallback?.Text = fallback ?? "";
        _service.SelectedIndexChanged += (_, _) =>
        {
            ShowService();
            Reset(_model, 0);
            if (_fallback is not null)
            {
                Reset(_fallback, 1);
            }
        };
        _testKeyButton.Click += (_, _) => _ = TestKeyAsync();
    }

    public CloudService Service => _services[_service.SelectedIndex];

    /// <summary>Adds this group's rows, label first.</summary>
    public void AddRows(Action<string, Control> row)
    {
        row("Cloud service", _service);
        row("Base URL", _baseUrl);
        row("Model", Flow(_model, ResetButton(_model, 0)));
        if (_fallback is not null)
        {
            row("Fallback model", Flow(_fallback, ResetButton(_fallback, 1)));
        }

        row("API key", _keyStatus);
        row("New key", Flow(_newKey, _testKeyButton));
        row("", _testResult);
    }

    /// <summary>
    /// The chosen service, custom URL, model and fallback, and a typed key; false with
    /// <paramref name="error"/> naming what to fix.
    /// </summary>
    public bool TryRead(out CloudChoice choice, out string? error)
    {
        choice = default;
        var model = _model.Text.Trim();
        var fallback = _fallback?.Text.Trim() ?? "";
        var key = _newKey.Text.Trim();
        error = !AppSettings.TryParseBaseUrl(_baseUrl.Text.Trim(), out var url)
                ? "The base URL must start with https:// (or http:// for a server on this PC) and have no query " +
                    "string or password."
            : !AppSettings.IsValidModel(model)
                ? "The model must be up to 100 visible characters, without spaces."
            : fallback.Length > 0 && !AppSettings.IsValidModel(fallback)
                ? "The fallback model must be up to 100 visible characters, without spaces."
            : SettingsInput.KeyError(key);
        if (error is not null)
        {
            return false;
        }

        // A named service keeps its own address; the custom one stays for when Custom is chosen again.
        var customUrl = Service == CloudService.Custom ? url! : _customUrl;
        choice = new(Service, customUrl, model, fallback.Length == 0 ? null : fallback, key.Length == 0 ? null : key);
        return true;
    }

    private static int IndexOf(IReadOnlyList<CloudService> services, CloudService service)
    {
        for (var i = 0; i < services.Count; i++)
        {
            if (services[i] == service)
            {
                return i;
            }
        }

        return -1;
    }

    private static Label Note(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth + 120, 0) };

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private IReadOnlyList<string> Suggestions()
    {
        var preset = CloudServices.For(Service);
        return _transcription ? preset.TranscriptionModels : preset.LanguageModels;
    }

    private Button ResetButton(ComboBox box, int index)
    {
        var reset = new Button { Text = "Reset", AutoSize = true };
        reset.Click += (_, _) => Reset(box, index);
        return reset;
    }

    // The service's suggested model at index, or blank for a custom endpoint that has none.
    private void Reset(ComboBox box, int index)
    {
        var suggestions = Suggestions();
        box.Text = index < suggestions.Count ? suggestions[index] : "";
    }

    private void ShowService()
    {
        var preset = CloudServices.For(Service);
        _baseUrl.Enabled = Service == CloudService.Custom;
        _baseUrl.Text = (preset.BaseUrl ?? _customUrl).AbsoluteUri;

        foreach (var box in new[] { _model, _fallback })
        {
            box?.Items.Clear();
            box?.Items.AddRange([.. Suggestions()]);
        }

        _keyStatus.Text = SettingsInput.KeyStatus(_keyFor(Service), CloudServices.OverrideVariable(Service))
            + (preset.KeysAt is { } at ? $" Keys: {at}" : " A server on this PC may need none.");
        _testResult.Text = "";
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task TestKeyAsync()
    {
        var typed = _newKey.Text.Trim();
        if (SettingsInput.KeyError(typed) is { } keyError)
        {
            _testResult.Text = keyError;
            return;
        }

        if (!AppSettings.TryParseBaseUrl(_baseUrl.Text.Trim(), out var url))
        {
            _testResult.Text = "Fix the base URL first.";
            return;
        }

        var key = typed.Length > 0 ? typed : _keyFor(Service).Key;
        if (key is null && !(Service == CloudService.Custom && url.IsLoopback))
        {
            _testResult.Text = "Paste a key first.";
            return;
        }

        _testKeyButton.Enabled = false;
        _testResult.Text = "Testing...";
        try
        {
            _testResult.Text = SettingsInput.KeyTestMessage(await _testKey(Service, key, url, _closing));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while the test ran.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.ApiKeyTestFailed, e);
            _testResult.Text = "The key couldn't be checked.";
        }
        finally
        {
            if (!_testKeyButton.IsDisposed)
            {
                _testKeyButton.Enabled = true;
            }
        }
    }
}

/// <param name="Service">The chosen service.</param>
/// <param name="CustomUrl">The custom endpoint (the service's own address for a named service).</param>
/// <param name="Model">The model.</param>
/// <param name="Fallback">The fallback model, or null.</param>
/// <param name="NewKey">A typed key for <paramref name="Service"/>, or null.</param>
internal readonly record struct CloudChoice(
    CloudService Service, Uri CustomUrl, string Model, string? Fallback, string? NewKey)
{
    // The generated ToString would print the key.
    public override string ToString() => $"CloudChoice {{ Service = {Service}, NewKey = {NewKey is not null} }}";
}
