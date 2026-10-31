using Naad.Cleanup;
using Naad.LanguageModel;
using Naad.Providers;
using Naad.Transcription;

using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>
/// The settings window's AI page: cleanup, where the language model runs, its download, and the
/// cloud service. Like the window, it only shows fields; <see cref="SettingsInput"/> and
/// <see cref="CloudServiceFields"/> check them. Create and use it on the UI thread.
/// </summary>
internal sealed class LanguageModelPanel : TableLayoutPanel
{

    private readonly Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> _download;
    private readonly Func<string?, string, CancellationToken, Task<PromptTest>> _testPrompt;
    private readonly CancellationToken _closing;
    private readonly CloudServiceFields _cloud;

    private readonly ComboBox _cleanup = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly TextBox _outputLanguage = new() { Width = 160 };
    private readonly CheckBox _guard = new()
    {
        Text = "Paste the text as said when the AI seems to answer it instead of tidying it",
        AutoSize = true,
    };

    private readonly Button _promptButton = new() { Text = "Edit prompt...", AutoSize = true };
    private readonly Button _testModelButton = new() { Text = "Test model", AutoSize = true };
    private readonly Label _testModelResult = Note("");
    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly ComboBox _localModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly Label _modelStatus = Note("");
    private readonly Button _downloadButton = new() { Text = "Download model", AutoSize = true };
    private readonly NumericUpDown _timeout = new()
    {
        Minimum = AppSettings.MinTimeoutSeconds,
        Maximum = AppSettings.MaxTimeoutSeconds,
        Width = 60,
    };

    private string? _customPrompt;
    private string? _promptVersion;

    /// <param name="current">The settings in use.</param>
    /// <param name="keyFor">Where a service's key comes from.</param>
    /// <param name="download">Downloads a model file, reporting progress.</param>
    /// <param name="testKey">Checks a service's key.</param>
    /// <param name="testPrompt">Sends instructions (null for the default) and a sample to the saved model.</param>
    /// <param name="cooldownFor">A cloud model's running rate limit, if any.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public LanguageModelPanel(
        AppSettings current,
        Func<CloudService, ApiKeyResult> keyFor,
        Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> download,
        Func<CloudService, string?, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        Func<string?, string, CancellationToken, Task<PromptTest>> testPrompt,
        Func<string, Cooldown?> cooldownFor,
        CancellationToken closing)
    {
        _testPrompt = testPrompt;
        _customPrompt = current.CleanupPrompt;
        _promptVersion = current.CleanupPromptVersion;
        _download = download;
        _closing = closing;
        _cloud = new(
            CloudServices.ForLanguageModel,
            current.LanguageModelService,
            current.LanguageModelBaseUrl,
            current.LanguageModelName,
            current.LanguageModelFallbackName,
            withFallback: true,
            keyFor,
            testKey,
            closing);
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);

        _provider.Items.AddRange(["This PC: offline and free", "Cloud: the service below"]);
        _provider.SelectedIndex = (int)current.LanguageModelProvider;
        _localModel.Items.AddRange(["Small: fast on any PC", "Large: more accurate, needs a GPU"]);
        _localModel.SelectedIndex = (int)current.LocalLanguageModel;
        _cleanup.Items.AddRange(["Off: paste as said", "Basic: instant rules", "AI: the language model"]);
        _cleanup.SelectedIndex = (int)current.Cleanup;
        _outputLanguage.Text = current.OutputLanguage ?? "";
        _guard.Checked = current.InstructionGuard;
        _timeout.Value = current.LanguageModelTimeoutSeconds;
        ShowModelStatus();

        Row(this, "Cleanup", _cleanup);
        Row(this, "", Note("AI removes filler words and fixes grammar. Until a model is set up below, it uses the " +
            "instant rules. A slow or failed AI step pastes the text as said."));
        Row(this, "Prompt", Flow(_promptButton, _testModelButton));
        Row(this, "", _testModelResult);
        Row(this, "Write in", Flow(_outputLanguage, Note("A language such as German; blank keeps yours.")));
        Row(this, "", _guard);
        Row(this, "Wait up to", Flow(_timeout, Note("seconds for the AI, then paste as said")));
        Row(this, "AI runs on", _provider);
        Row(this, "Model on this PC", _localModel);
        Row(this, "", Flow(_modelStatus, _downloadButton));
        _cloud.AddRows((label, control) => Row(this, label, control));
        Row(this, "", Note("Offline, a cloud choice uses the model on this PC if it is downloaded."));
        foreach (var name in new[] { current.LanguageModelName, current.LanguageModelFallbackName })
        {
            if (name is not null && SettingsInput.CooldownMessage(name, cooldownFor(name)) is { } cooling)
            {
                Row(this, "", Note(cooling));
            }
        }

        _promptButton.Click += (_, _) => EditPrompt();
        _testModelButton.Click += (_, _) => _ = TestModelAsync();
        _localModel.SelectedIndexChanged += (_, _) => ShowModelStatus();
        _downloadButton.Click += (_, _) => _ = DownloadAsync();
    }

    private LocalModel ChosenFile => LanguageModelFiles.For((LocalLanguageModelChoice)_localModel.SelectedIndex);

    /// <summary>
    /// <paramref name="chosen"/> with this page's fields applied, and a key typed for the chosen
    /// service, or false with <paramref name="error"/> naming what to fix.
    /// </summary>
    public bool TryApply(AppSettings chosen, out AppSettings settings, out CloudChoice cloud, out string? error)
    {
        settings = chosen;
        var language = _outputLanguage.Text.Trim();
        if (!_cloud.TryRead(out cloud, out error))
        {
            return false;
        }

        if (language.Length > 0 && !AppSettings.IsValidOutputLanguage(language))
        {
            error = "The output language must be a language name such as German, or blank.";
            return false;
        }

        settings = chosen with
        {
            LanguageModelProvider = (LanguageModelProvider)_provider.SelectedIndex,
            LocalLanguageModel = (LocalLanguageModelChoice)_localModel.SelectedIndex,
            LanguageModelService = cloud.Service,
            LanguageModelBaseUrl = cloud.CustomUrl,
            LanguageModelName = cloud.Model,
            LanguageModelFallbackName = cloud.Fallback,
            LanguageModelTimeoutSeconds = (int)_timeout.Value,
            OutputLanguage = language.Length == 0 ? null : language,
            Cleanup = (CleanupLevel)_cleanup.SelectedIndex,
            CleanupPrompt = _customPrompt,
            CleanupPromptVersion = _promptVersion,
            InstructionGuard = _guard.Checked,
        };
        return true;
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
}
