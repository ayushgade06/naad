using Naad.Cleanup;
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
    private readonly Func<string?, string, CancellationToken, Task<PromptTest>> _testPrompt;
    private readonly CancellationToken _closing;

    private readonly ComboBox _cleanup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly TextBox _fallback = new() { Width = FieldWidth };
    private readonly TextBox _outputLanguage = new() { Width = 160 };
    private readonly CheckBox _guard = new()
    {
        Text = "Paste the text as said when the AI seems to answer it instead of tidying it",
        AutoSize = true,
    };

    private readonly Button _promptButton = new() { Text = "Edit prompt...", AutoSize = true };
    private readonly Button _testModelButton = new() { Text = "Test model", AutoSize = true };
    private readonly Label _testModelResult = Note("");
    private string? _customPrompt;
    private string? _promptVersion;

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
    /// <param name="testPrompt">Sends instructions (null for the default) and a sample to the saved model.</param>
    /// <param name="cooldownFor">A cloud model's running rate limit, if any.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public LanguageModelPanel(
        AppSettings current,
        ApiKeyResult key,
        Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> download,
        Func<string, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        Func<string?, string, CancellationToken, Task<PromptTest>> testPrompt,
        Func<string, Cooldown?> cooldownFor,
        CancellationToken closing)
    {
        _testPrompt = testPrompt;
        _customPrompt = current.CleanupPrompt;
        _promptVersion = current.CleanupPromptVersion;
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
        _cleanup.Items.AddRange(["Off: paste as said", "Basic: instant rules", "AI: the language model"]);
        _cleanup.SelectedIndex = (int)current.Cleanup;
        _fallback.Text = current.LanguageModelFallbackName ?? "";
        _outputLanguage.Text = current.OutputLanguage ?? "";
        _guard.Checked = current.InstructionGuard;
        ShowModelStatus();

        Row("Cleanup", _cleanup);
        Row("", Note("AI removes filler words and fixes grammar. Until a model is set up below, it uses the " +
            "instant rules. A slow or failed AI step pastes the text as said."));
        Row("Prompt", Flow(_promptButton, _testModelButton));
        Row("", _testModelResult);
        Row("Write in", Flow(_outputLanguage, Note("A language such as German; blank keeps yours.")));
        Row("", _guard);
        Row("AI runs on", _provider);
        Row("Model on this PC", _localModel);
        Row("", Flow(_modelStatus, _downloadButton));
        Row("Base URL", _baseUrl);
        Row("Model", _model);
        Row("Fallback model", _fallback);
        Row("API key", Note(SettingsInput.KeyStatus(key, SecretStore.LanguageModelOverrideVariable)));
        Row("New key", Flow(_newKey, _testKeyButton));
        Row("", _testResult);
        Row("", Note("A server on this PC, such as Ollama (http://localhost:11434/v1), needs no key."));
        foreach (var name in new[] { current.LanguageModelName, current.LanguageModelFallbackName })
        {
            if (name is not null && SettingsInput.CooldownMessage(name, cooldownFor(name)) is { } cooling)
            {
                Row("", Note(cooling));
            }
        }

        _promptButton.Click += (_, _) => EditPrompt();
        _testModelButton.Click += (_, _) => _ = TestModelAsync();

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

        if (!SettingsInput.TryBuildLanguageModel(
            chosen, _baseUrl.Text, _model.Text, _fallback.Text, _outputLanguage.Text, out var built, out error))
        {
            return false;
        }

        settings = built with
        {
            LanguageModelProvider = (LanguageModelProvider)_provider.SelectedIndex,
            LocalLanguageModel = (LocalLanguageModelChoice)_localModel.SelectedIndex,
            Cleanup = (CleanupLevel)_cleanup.SelectedIndex,
            CleanupPrompt = _customPrompt,
            CleanupPromptVersion = _promptVersion,
            InstructionGuard = _guard.Checked,
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

    private void EditPrompt()
    {
        using var dialog = new CleanupPromptDialog(_customPrompt, _promptVersion, _testPrompt);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _customPrompt = dialog.Custom;
            _promptVersion = dialog.Version;
        }
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task TestModelAsync()
    {
        _testModelButton.Enabled = false;
        _testModelResult.Text = "Waiting for the model (uses the saved AI settings)...";
        try
        {
            var result = await _testPrompt(_customPrompt, CleanupPromptDialog.Sample, _closing);
            _testModelResult.Text = SettingsInput.PromptTestMessage(result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while the test ran.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.CleanupPromptTested, e);
            _testModelResult.Text = "The test couldn't run.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _testModelButton.Enabled = true;
            }
        }
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

        if (!AppSettings.TryParseBaseUrl(_baseUrl.Text.Trim(), out var url))
        {
            _testResult.Text = "Fix the base URL first.";
            return;
        }

        if ((typed.Length > 0 ? typed : _key.Key) is not { } key)
        {
            _testResult.Text = url.IsLoopback
                ? "A server on this PC needs no key here."
                : "Paste a key first.";
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
