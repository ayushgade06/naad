using Naad.Hotkey;
using Naad.Transcription;
using Naad.Tray;

namespace Naad.Settings;

/// <summary>
/// Naad's settings window. It stays thin: <see cref="SettingsInput"/> checks the fields and words
/// the messages, and Program's callbacks save and apply. The API key is write-only here: the
/// window shows where the key comes from, never the key. Create, use and close it on the UI thread.
/// </summary>
internal sealed class SettingsWindow : Form
{
    private const string WindowsDefaultMicrophone = "Windows default";
    private const string CapturePrompt = "Press the new shortcut, then release it. Esc cancels.";
    private const int FieldWidth = 300;

    private readonly AppSettings _current;
    private readonly ApiKeyResult _key;
    private readonly Func<string, Uri, CancellationToken, Task<KeyTestResult>> _testKey;
    private readonly Func<SettingsChange, string?> _save;
    private readonly Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> _downloadModel;
    private readonly LanguageModelPanel _languageModel;
    private readonly Func<bool> _beginCapture;
    private readonly Action _cancelCapture;
    private readonly CancellationTokenSource _closing = new();
    private readonly Icon _icon = TrayIcon.LoadIcon();

    // Index 0 is the Windows default (null); the rest are device names.
    private readonly List<string?> _microphones = [null];

    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly ComboBox _localModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly Label _modelStatus = Note();
    private readonly Button _downloadModelButton = new() { Text = "Download model", AutoSize = true };
    private readonly TextBox _newKey = new() { UseSystemPasswordChar = true, Width = FieldWidth };
    private readonly Button _testKeyButton = new() { Text = "Test key", AutoSize = true };
    private readonly Label _testResult = Note();
    private readonly Label _shortcutText = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _changeShortcut = new() { Text = "Change...", AutoSize = true };
    private readonly ComboBox _microphone = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly TextBox _language = new() { Width = 60 };
    private readonly TextBox _vocabulary = new() { Width = FieldWidth };
    private readonly TextBox _baseUrl = new() { Width = FieldWidth };
    private readonly TextBox _model = new() { Width = FieldWidth };
    private readonly CheckBox _playSounds = new()
    {
        Text = "Play a tone when listening starts and stops",
        AutoSize = true,
    };

    private readonly CheckBox _fileLogging = new()
    {
        Text = "Write a diagnostic log (never what you say)",
        AutoSize = true,
    };

    private readonly CheckBox _handsFree = new()
    {
        Text = "Hands-free: press the shortcut to start, and again to stop",
        AutoSize = true,
    };

    private readonly CheckBox _startAtLogin = new() { Text = "Start Naad when I sign in", AutoSize = true };

    private readonly Label _error = Note();
    private Chord _shortcut;
    private bool _capturing;

    /// <param name="current">The settings in use.</param>
    /// <param name="key">Where the key in use comes from; the key itself is only used for Test key.</param>
    /// <param name="languageModelKey">The same for the language model's key.</param>
    /// <param name="microphones">The connected recording devices' names.</param>
    /// <param name="startsAtLogin">Whether Naad starts when the user signs in.</param>
    /// <param name="downloadModel">Downloads a model file to run on this PC, reporting progress.</param>
    /// <param name="testKey">Checks a key against a base URL.</param>
    /// <param name="save">Saves and applies the change. Returns what went wrong, or null.</param>
    /// <param name="beginCapture">Starts capturing a shortcut; false if a dictation is running.</param>
    /// <param name="cancelCapture">Stops a capture early.</param>
    public SettingsWindow(
        AppSettings current,
        ApiKeyResult key,
        ApiKeyResult languageModelKey,
        IReadOnlyList<string> microphones,
        bool startsAtLogin,
        Func<LocalModel, IProgress<double>, CancellationToken, Task<ModelDownloadResult>> downloadModel,
        Func<string, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        Func<SettingsChange, string?> save,
        Func<bool> beginCapture,
        Action cancelCapture)
    {
        _current = current;
        _key = key;
        _testKey = testKey;
        _save = save;
        _downloadModel = downloadModel;
        _beginCapture = beginCapture;
        _cancelCapture = cancelCapture;
        _shortcut = current.DictationShortcut;

        Text = "Naad settings";
        Icon = _icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        FillFields(microphones);
        _startAtLogin.Checked = startsAtLogin;
        _languageModel = new(current, languageModelKey, downloadModel, testKey, _closing.Token);
        Controls.Add(BuildLayout());

        _testKeyButton.Click += (_, _) => _ = TestKeyAsync();
        _downloadModelButton.Click += (_, _) => _ = DownloadModelAsync();
        _localModel.SelectedIndexChanged += (_, _) => ShowModelStatus();
        _changeShortcut.Click += (_, _) => BeginCapture();

        // Keys typed in other apps would be swallowed while capturing, so leaving the window ends it.
        Deactivate += (_, _) => CancelCapture();
        FormClosed += (_, _) =>
        {
            CancelCapture();
            _closing.Cancel();
        };
    }

    /// <summary>The hook finished a capture this window started. Call on the UI thread.</summary>
    public void ShowCapture(CaptureStep step)
    {
        if (!_capturing)
        {
            return;
        }

        if (step.Chord is { } chord)
        {
            _shortcut = chord;
        }

        EndCapture();
        _error.Text = step.Status == CaptureStatus.Rejected ? "That key can't be part of a shortcut. Try another." : "";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label Note() => new() { AutoSize = true, MaximumSize = new Size(FieldWidth + 120, 0) };

    private void FillFields(IReadOnlyList<string> microphones)
    {
        _microphone.Items.Add(WindowsDefaultMicrophone);
        foreach (var name in microphones)
        {
            _microphones.Add(name);
            _microphone.Items.Add(name);
        }

        // A chosen device that is unplugged stays selectable, so saving other changes keeps it.
        if (_current.Microphone is { } chosen && !microphones.Contains(chosen))
        {
            _microphones.Add(chosen);
            _microphone.Items.Add($"{chosen} (not connected)");
        }

        _microphone.SelectedIndex = _microphones.IndexOf(_current.Microphone);
        _provider.Items.AddRange(["Cloud: the OpenAI-compatible service below", "This PC: Whisper, offline and free"]);
        _provider.SelectedIndex = (int)_current.TranscriptionProvider;
        _localModel.Items.AddRange(["Base: fast on any PC", "Large v3 Turbo: more accurate, needs a GPU"]);
        _localModel.SelectedIndex = (int)_current.LocalModel;
        ShowModelStatus();
        _shortcutText.Text = _shortcut.Describe();
        _language.Text = _current.Language ?? "";
        _vocabulary.Text = _current.Vocabulary ?? "";
        _handsFree.Checked = _current.HandsFree;
        _baseUrl.Text = _current.TranscriptionBaseUrl.AbsoluteUri;
        _model.Text = _current.TranscriptionModel;
        _playSounds.Checked = _current.PlaySounds;
        _fileLogging.Checked = _current.FileLogging;
    }

    // Two pages, so the window fits a laptop screen; Save and Cancel apply to both.
    private TableLayoutPanel BuildLayout()
    {
        var dictation = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        save.Click += (_, _) => Save();

        // The window is modeless (Show, not ShowDialog), so a DialogResult wouldn't close it.
        cancel.Click += (_, _) => Close();
        AcceptButton = save;
        CancelButton = cancel;

        Row(dictation, "Transcribe with", _provider);
        Row(dictation, "Model on this PC", _localModel);
        Row(dictation, "", Flow(_modelStatus, _downloadModelButton));
        Row(dictation, "API key", Note(SettingsInput.KeyStatus(_key)));
        Row(dictation, "New key", Flow(_newKey, _testKeyButton));
        Row(dictation, "", _testResult);
        Row(dictation, "Shortcut", Flow(_shortcutText, _changeShortcut));
        Row(dictation, "", _handsFree);
        Row(dictation, "Microphone", _microphone);
        Row(dictation, "Language", Flow(_language, Note("Code such as en or hi; blank detects it.")));
        Row(dictation, "Vocabulary", _vocabulary);
        Row(dictation, "", Note("Names and terms you use, separated by commas. Sent with each recording."));
        Row(dictation, "Base URL", _baseUrl);
        Row(dictation, "Model", _model);
        Row(dictation, "", _playSounds);
        Row(dictation, "", _fileLogging);
        Row(dictation, "", _startAtLogin);
        var tabs = new TabControl();
        tabs.TabPages.Add(Page("Dictation", dictation));
        tabs.TabPages.Add(Page("AI", _languageModel));
        var largest = new Size(
            Math.Max(dictation.PreferredSize.Width, _languageModel.PreferredSize.Width),
            Math.Max(dictation.PreferredSize.Height, _languageModel.PreferredSize.Height));
        tabs.Size = largest + (tabs.Size - tabs.DisplayRectangle.Size) + new Size(8, 8);
        table.Controls.Add(tabs);
        table.Controls.Add(_error);
        table.Controls.Add(Flow(save, cancel));
        return table;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    private static Label Note(string text)
    {
        var label = Note();
        label.Text = text;
        return label;
    }

    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private static void Row(TableLayoutPanel table, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        table.Controls.Add(control);
    }

    private WhisperModel ChosenModel => (WhisperModel)_localModel.SelectedIndex;

    // Reads the disk, so a model downloaded since the window opened counts.
    private void ShowModelStatus()
    {
        var model = LocalModel.For(ChosenModel);
        var ready = model.IsReady();
        _modelStatus.Text = SettingsInput.ModelStatus(model.Size, ready);
        _downloadModelButton.Visible = !ready;
    }

    private void BeginCapture()
    {
        if (_capturing || !_beginCapture())
        {
            return;
        }

        _capturing = true;
        _changeShortcut.Enabled = false;
        _shortcutText.Text = CapturePrompt;
    }

    private void CancelCapture()
    {
        if (_capturing)
        {
            _cancelCapture();
            EndCapture();
        }
    }

    private void EndCapture()
    {
        _capturing = false;
        _changeShortcut.Enabled = true;
        _shortcutText.Text = _shortcut.Describe();
    }

    private void Save()
    {
        CancelCapture();
        var chosen = _current with
        {
            DictationShortcut = _shortcut,
            Microphone = _microphones[_microphone.SelectedIndex],
            PlaySounds = _playSounds.Checked,
            FileLogging = _fileLogging.Checked,
            HandsFree = _handsFree.Checked,
            TranscriptionProvider = (TranscriptionProvider)_provider.SelectedIndex,
            LocalModel = ChosenModel,
        };
        if (chosen.TranscriptionProvider == TranscriptionProvider.Local && !LocalModel.For(ChosenModel).IsReady())
        {
            _error.Text = "Download the model first, or transcribe with the cloud service.";
            return;
        }

        if (!SettingsInput.TryBuild(
            chosen, _baseUrl.Text, _model.Text, _language.Text, _vocabulary.Text, out var settings, out var error))
        {
            _error.Text = error;
            return;
        }

        if (SettingsInput.KeyError(_newKey.Text) is { } keyError)
        {
            _error.Text = keyError;
            return;
        }

        if (!_languageModel.TryApply(settings, out settings, out var newLanguageModelKey, out error))
        {
            _error.Text = error;
            return;
        }

        var newKey = _newKey.Text.Trim() is { Length: > 0 } typed ? typed : null;
        if (_save(new(settings, newKey, newLanguageModelKey, _startAtLogin.Checked)) is { } problem)
        {
            _error.Text = problem;
            return;
        }

        Close();
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task DownloadModelAsync()
    {
        // The choice can't change mid-download: the progress and the result belong to this model.
        var choice = ChosenModel;
        _downloadModelButton.Enabled = false;
        _localModel.Enabled = false;
        var progress = new Progress<double>(fraction => _modelStatus.Text = $"Downloading... {fraction:P0}");
        try
        {
            var result = await _downloadModel(LocalModel.For(choice), progress, _closing.Token);
            _modelStatus.Text = SettingsInput.ModelDownloadMessage(result);
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
                _downloadModelButton.Enabled = true;
                _localModel.Enabled = true;
                _downloadModelButton.Visible = !LocalModel.For(choice).IsReady();
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
            _testResult.Text = SettingsInput.KeyTestMessage(await _testKey(key, url, _closing.Token));
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
