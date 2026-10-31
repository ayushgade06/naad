using Naad.LanguageModel;
using Naad.Transcription;

namespace Naad.Settings;

/// <summary>
/// The settings window's AI page: where the language model runs, its download, and the cloud
/// endpoint and key. Like the window, it only shows fields; <see cref="SettingsInput"/> checks
/// them. The key is write-only here. Create and use it on the UI thread.
/// </summary>
internal sealed class LanguageModelPanel : TableLayoutPanel
{
    private const int FieldWidth = 300;

    private readonly ApiKeyResult _key;
    private readonly Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> _download;
    private readonly Func<string, Uri, CancellationToken, Task<KeyTestResult>> _testKey;
    private readonly CancellationToken _closing;

    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly ComboBox _localModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly Label _modelStatus = Note("");
    private readonly Button _downloadButton = new() { Text = "Download model", AutoSize = true };
    private readonly TextBox _baseUrl = new() { Width = FieldWidth };
    private readonly TextBox _model = new() { Width = FieldWidth };
    private readonly TextBox _newKey = new() { UseSystemPasswordChar = true, Width = FieldWidth };
    private readonly Button _testKeyButton = new() { Text = "Test key", AutoSize = true };
    private readonly Label _testResult = Note("");

    /// <param name="current">The settings in use.</param>
    /// <param name="key">Where the language model key comes from.</param>
    /// <param name="download">Downloads a model file, reporting progress.</param>
    /// <param name="testKey">Checks a key against a base URL.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public LanguageModelPanel(
        AppSettings current,
        ApiKeyResult key,
        Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> download,
        Func<string, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        CancellationToken closing)
    {
        _key = key;
        _download = download;
        _testKey = testKey;
        _closing = closing;
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);

        _provider.Items.AddRange(["This PC: offline and free", "Cloud: the OpenAI-compatible service below"]);
        _provider.SelectedIndex = (int)current.LanguageModelProvider;
        _localModel.Items.AddRange(["Small: fast on any PC", "Large: more accurate, needs a GPU"]);
        _localModel.SelectedIndex = (int)current.LocalLanguageModel;
        _baseUrl.Text = current.LanguageModelBaseUrl.AbsoluteUri;
        _model.Text = current.LanguageModelName;
        ShowModelStatus();

        Row("AI runs on", _provider);
        Row("Model on this PC", _localModel);
        Row("", Flow(_modelStatus, _downloadButton));
        Row("Base URL", _baseUrl);
        Row("Model", _model);
        Row("API key", Note(SettingsInput.KeyStatus(key, SecretStore.LanguageModelOverrideVariable)));
        Row("New key", Flow(_newKey, _testKeyButton));
        Row("", _testResult);
        Row("", Note("A server on this PC, such as Ollama (http://localhost:11434/v1), needs no key."));

        _localModel.SelectedIndexChanged += (_, _) => ShowModelStatus();
        _downloadButton.Click += (_, _) => _ = DownloadAsync();
        _testKeyButton.Click += (_, _) => _ = TestKeyAsync();
    }

    private LocalModel ChosenFile => LanguageModelFiles.For((LocalLanguageModelChoice)_localModel.SelectedIndex);

    /// <summary>
    /// <paramref name="chosen"/> with this page's fields applied, or false with
    /// <paramref name="error"/> naming what to fix.
    /// </summary>
    public bool TryApply(AppSettings chosen, out AppSettings settings, out string? newKey, out string? error)
    {
        newKey = _newKey.Text.Trim() is { Length: > 0 } typed ? typed : null;
        error = SettingsInput.KeyError(_newKey.Text);
        settings = chosen;
        if (error is not null)
        {
            return false;
        }

        if (!SettingsInput.TryBuildLanguageModel(chosen, _baseUrl.Text, _model.Text, out var built, out error))
        {
            return false;
        }

        settings = built with
        {
            LanguageModelProvider = (LanguageModelProvider)_provider.SelectedIndex,
            LocalLanguageModel = (LocalLanguageModelChoice)_localModel.SelectedIndex,
        };
        return true;
    }

    private static Label Note(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth + 120, 0) };

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private void Row(string label, Control control)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        Controls.Add(control);
    }

    // Reads the disk, so a model downloaded since the window opened counts.
    private void ShowModelStatus()
    {
        var file = ChosenFile;
        var ready = file.IsReady();
        _modelStatus.Text = SettingsInput.LanguageModelStatus(file.Size, ready);
        _downloadButton.Visible = !ready;
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task DownloadAsync()
    {
        // The choice can't change mid-download: the progress and the result belong to this file.
        var file = ChosenFile;
        _downloadButton.Enabled = false;
        _localModel.Enabled = false;
        var progress = new Progress<double>(fraction => _modelStatus.Text = $"Downloading... {fraction:P0}");
        try
        {
            var result = await _download(file, progress, _closing);
            _modelStatus.Text = result.Outcome == ModelDownloadOutcome.Downloaded
                ? "Downloaded. AI steps now run on this PC, offline."
                : SettingsInput.ModelDownloadMessage(result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed during the download; nothing partial is kept.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.LocalModelDownloadFailed, e);
            _modelStatus.Text = SettingsInput.ModelDownloadMessage(new(ModelDownloadOutcome.DiskError));
        }
        finally
        {
            if (!IsDisposed)
            {
                _downloadButton.Enabled = true;
                _localModel.Enabled = true;
                _downloadButton.Visible = !file.IsReady();
            }
        }
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

        if ((typed.Length > 0 ? typed : _key.Key) is not { } key)
        {
            _testResult.Text = "Paste a key first.";
            return;
        }

        if (!AppSettings.TryParseBaseUrl(_baseUrl.Text.Trim(), out var url))
        {
            _testResult.Text = "Fix the base URL first.";
            return;
        }

        _testKeyButton.Enabled = false;
        _testResult.Text = "Testing...";
        try
        {
            _testResult.Text = SettingsInput.KeyTestMessage(await _testKey(key, url, _closing));
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
            if (!IsDisposed)
            {
                _testKeyButton.Enabled = true;
            }
        }
    }
}
