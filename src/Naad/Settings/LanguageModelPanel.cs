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
    private readonly Func<string?, string, CancellationToken, Task<IReadOnlyList<ModelTest>>> _testModels;
    private readonly Func<string?, string, CancellationToken, Task<PromptTest>> _testContext;
    private readonly ComboBox _contextModel = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = FieldWidth };
    private readonly Button _contextModelReset = new() { Text = "Reset", AutoSize = true };
    private readonly Button _contextPromptButton = new() { Text = "Context prompt...", AutoSize = true };
    private readonly CheckBox _windowTitle = new()
    {
        Text = "Also send the window\u2019s title (it often holds names and subjects)",
        AutoSize = true,
    };

    private string? _contextPrompt;
    private string? _contextPromptVersion;
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
    private readonly Button _testModelButton = new() { Text = "Test models", AutoSize = true };
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

    private readonly CheckBox _context = new()
    {
        Text = "Read the app\u2019s name and the text near the cursor, to spell names right (never passwords)",
        AutoSize = true,
        MaximumSize = new Size(FieldWidth + 120, 0),
    };

    private readonly TextBox _excluded = new() { Width = FieldWidth };
    private readonly ComboBox _edit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly CheckBox _screenshot = new()
    {
        Text = "Cloud only: also send a screenshot of the window to the cloud model",
        AutoSize = true,
    };

    private readonly ComboBox _screenshotSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
    private readonly CheckBox _wholeDesktop = new()
    {
        Text = "Capture the whole desktop when the window can\u2019t be captured",
        AutoSize = true,
    };

    private string? _customPrompt;
    private string? _promptVersion;

    /// <param name="current">The settings in use.</param>
    /// <param name="keyFor">Where a service's key comes from.</param>
    /// <param name="download">Downloads a model file, reporting progress.</param>
    /// <param name="testKey">Checks a service's key.</param>
    /// <param name="testPrompt">Sends instructions (null for the default) and a sample to the saved model.</param>
    /// <param name="testModels">Sends them to each saved model in turn.</param>
    /// <param name="testContext">Sends context instructions and a sample to the saved context model.</param>
    /// <param name="cooldownFor">A cloud model's running rate limit, if any.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public LanguageModelPanel(
        AppSettings current,
        Func<CloudService, ApiKeyResult> keyFor,
        Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> download,
        Func<CloudService, string?, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        Func<string?, string, CancellationToken, Task<PromptTest>> testPrompt,
        Func<string?, string, CancellationToken, Task<IReadOnlyList<ModelTest>>> testModels,
        Func<string?, string, CancellationToken, Task<PromptTest>> testContext,
        Func<string, Cooldown?> cooldownFor,
        CancellationToken closing)
    {
        _testPrompt = testPrompt;
        _testModels = testModels;
        _testContext = testContext;
        _contextPrompt = current.ContextPrompt;
        _contextPromptVersion = current.ContextPromptVersion;
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
        _context.Checked = current.Context;
        _excluded.Text = string.Join(", ", current.ContextExcludedApps);
        _edit.Items.AddRange(["Off: dictation replaces a selection", "Automatic: speech over a selection edits it",
            $"Manual: hold {current.EditModeKey} with the shortcut to edit"]);
        _edit.SelectedIndex = (int)current.EditMode;
        _screenshot.Checked = current.ContextScreenshot;
        _screenshotSize.Items.AddRange(["1024", "768", "640", "512"]);
        _screenshotSize.SelectedItem =
            current.ScreenshotSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _wholeDesktop.Checked = current.ScreenshotWholeDesktop;
        Row(this, "Edit Mode", _edit);
        Row(this, "", Note("Select text, hold the shortcut and say how to change it (\u201cmake this shorter\u201d). " +
            "If the edit fails, your text is left as it was."));
        Row(this, "Context", _context);
        Row(this, "Never read", Flow(_excluded, Note("App names, such as keepass")));
        _windowTitle.Checked = current.ContextWindowTitle;
        Row(this, "", _windowTitle);
        Row(this, "Context model", Flow(_contextModel, _contextModelReset, _contextPromptButton));
        Row(this, "", Note("Sums up what you are doing while you speak, as a hint for cleanup. In the cloud it is " +
            "its own model, one that reads images for the screenshot; on this PC, the model there."));
        Row(this, "", _screenshot);
        Row(this, "Screenshot", Flow(_screenshotSize, _wholeDesktop));
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
        _contextPromptButton.Click += (_, _) => EditContextPrompt();
        _contextModel.Text = current.ContextModelName ?? "";
        _contextModel.DropDown += (_, _) =>
        {
            _contextModel.Items.Clear();
            _contextModel.Items.AddRange([.. CloudServices.ContextModels(_cloud.Service)]);
        };
        _contextModelReset.Click += (_, _) => _contextModel.Text = "";
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

        // Blank, or the service's own suggestion, follows the service.
        var contextModel = _contextModel.Text.Trim();
        if (contextModel.Length > 0 && !AppSettings.IsValidModel(contextModel))
        {
            error = "The context model must be a model id, or blank for the service's suggestion.";
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
            ContextModelName = contextModel.Length == 0 ? null : contextModel,
            ContextPrompt = _contextPrompt,
            ContextPromptVersion = _contextPromptVersion,
            ContextWindowTitle = _windowTitle.Checked,
            InstructionGuard = _guard.Checked,
            Context = _context.Checked,
            ContextExcludedApps = AppSettings.SplitWords(_excluded.Text),
            EditMode = (Naad.Context.EditMode)_edit.SelectedIndex,
            ContextScreenshot = _screenshot.Checked,
            ScreenshotSize = int.Parse((string)_screenshotSize.SelectedItem!,
                System.Globalization.CultureInfo.InvariantCulture),
            ScreenshotWholeDesktop = _wholeDesktop.Checked,
        };
        return true;
    }

    private void EditPrompt()
    {
        using var dialog = new PromptDialog(PromptKind.Cleanup, _customPrompt, _promptVersion, _testPrompt);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _customPrompt = dialog.Custom;
            _promptVersion = dialog.Version;
        }
    }

    private void EditContextPrompt()
    {
        using var dialog = new PromptDialog(PromptKind.Context, _contextPrompt, _contextPromptVersion, _testContext);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _contextPrompt = dialog.Custom;
            _contextPromptVersion = dialog.Version;
        }
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task TestModelAsync()
    {
        _testModelButton.Enabled = false;
        _testModelResult.Text = "Waiting for the models (uses the saved AI settings)...";
        try
        {
            var results = await _testModels(_customPrompt, PromptKind.Cleanup.Sample, _closing);
            _testModelResult.Text = string.Join(
                "\n", results.Select(r => $"{r.Model}: {SettingsInput.PromptTestMessage(r.Result)}"));
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
