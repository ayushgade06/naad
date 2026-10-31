using Naad.Hotkey;
using Naad.Providers;
using Naad.Transcription;
using Naad.Tray;
using Naad.Window;

using static Naad.Settings.FormLayout;

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

    private readonly AppSettings _current;
    private readonly SettingsWindowActions _actions;
    private readonly LanguageModelPanel _languageModel;
    private readonly GeneralPanel _general;
    private readonly bool _embedded;
    private readonly CloudServiceFields _cloud;
    private readonly CancellationTokenSource _closing = new();
    private readonly Icon _icon = TrayIcon.LoadIcon();

    // Index 0 is the Windows default (null); the rest are device names.
    private readonly List<string?> _microphones = [null];

    private readonly ComboBox _provider = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly ComboBox _localModel = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly Label _modelStatus = Note();
    private readonly Button _downloadModelButton = new() { Text = "Download model", AutoSize = true };
    private readonly Button _testTranscriptionButton = new() { Text = "Test", AutoSize = true };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Label _testTranscriptionResult = Note();
    private readonly NumericUpDown _timeout = new()
    {
        Minimum = AppSettings.MinTimeoutSeconds,
        Maximum = AppSettings.MaxTimeoutSeconds,

        // Three digits and the arrows.
        Width = 60,
    };
    private readonly Label _shortcutText = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _changeShortcut = new() { Text = "Change...", AutoSize = true };
    private readonly ComboBox _microphone = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly TextBox _language = new() { Width = 60 };
    private readonly TextBox _vocabulary = new() { Width = FieldWidth };
    private readonly CheckBox _handsFree = new()
    {
        Text = "Hands-free: press the shortcut to start, and again to stop",
        AutoSize = true,
    };

    private readonly Label _error = Note();
    private Chord _shortcut;
    private bool _capturing;

    /// <param name="current">The settings in use.</param>
    /// <param name="microphones">The connected recording devices' names.</param>
    /// <param name="startsAtLogin">Whether Naad starts when the user signs in.</param>
    /// <param name="actions">What the buttons do.</param>
    /// <param name="palette">The colours, for the dark theme; null keeps Windows' own.</param>
    /// <param name="embedded">Shown inside the main window: no frame, and Save keeps it open.</param>
    public SettingsWindow(
        AppSettings current,
        IReadOnlyList<string> microphones,
        bool startsAtLogin,
        SettingsWindowActions actions,
        Palette? palette = null,
        bool embedded = false)
    {
        _embedded = embedded;
        _current = current;
        _actions = actions;
        _shortcut = current.DictationShortcut;
        _cloud = new(
            CloudServices.ForTranscription,
            current.TranscriptionService,
            current.TranscriptionBaseUrl,
            current.TranscriptionModel,
            fallback: null,
            withFallback: false,
            actions.KeyFor,
            actions.TestKey,
            _closing.Token);

        Text = "Naad settings";
        Icon = _icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        FillFields(microphones);
        _general = new(current, startsAtLogin);
        _languageModel = new(
            current,
            actions.KeyFor,
            actions.DownloadModel,
            actions.TestKey,
            actions.TestPrompt,
            actions.CooldownFor,
            _closing.Token);
        Controls.Add(BuildLayout());
        if (embedded)
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            AutoSize = false;
            AutoScroll = true;
        }

        if (palette is not null)
        {
            FormLayout.ApplyPalette(this, palette);
        }

        _testTranscriptionButton.Click += (_, _) => _ = TestTranscriptionAsync();
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
            // Embedded, the form is disposed without closing: a capture must not keep swallowing
            // keys, and a running download or test must stop writing to it.
            CancelCapture();
            if (!_closing.IsCancellationRequested)
            {
                _closing.Cancel();
            }

            _closing.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

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
        _timeout.Value = _current.TranscriptionTimeoutSeconds;
    }

    // Two pages, so the window fits a laptop screen; Save and Cancel apply to both.
    private TableLayoutPanel BuildLayout()
    {
        var dictation = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(8) };
        var save = _saveButton;
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        save.Click += (_, _) => _ = SaveAsync();

        // The window is modeless (Show, not ShowDialog), so a DialogResult wouldn't close it.
        // Inside the main window there is nothing to cancel to: leaving the page drops changes.
        cancel.Click += (_, _) => Close();
        cancel.Visible = !_embedded;
        AcceptButton = save;

        // Embedded, Esc belongs to the main window; Close would only hide this page.
        if (!_embedded)
        {
            CancelButton = cancel;
        }

        // Ctrl+Enter saves from anywhere, even a field where Enter is text.
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter && _saveButton.Enabled)
            {
                e.SuppressKeyPress = true;
                _ = SaveAsync();
            }
        };

        Row(dictation, "Transcribe with", _provider);
        Row(dictation, "Model on this PC", _localModel);
        Row(dictation, "", Flow(_modelStatus, _downloadModelButton));
        Row(dictation, "Shortcut", Flow(_shortcutText, _changeShortcut));
        Row(dictation, "", _handsFree);
        Row(dictation, "Microphone", _microphone);
        Row(dictation, "Language", Flow(_language, Note("Code such as en or hi; blank detects it.")));
        Row(dictation, "Vocabulary", _vocabulary);
        Row(dictation, "", Note("Names and terms you use, separated by commas. Sent with each recording."));
        _cloud.AddRows((label, control) => Row(dictation, label, control));
        Row(dictation, "Wait up to", Flow(_timeout, Note("seconds for the cloud service")));
        Row(dictation, "Transcription", Flow(_testTranscriptionButton, _testTranscriptionResult));
        var tabs = new TabControl();
        tabs.TabPages.Add(Page("Dictation", dictation));
        tabs.TabPages.Add(Page("AI", _languageModel));
        tabs.TabPages.Add(Page("General", _general));
        Control[] pages = [dictation, _languageModel, _general];
        var largest = new Size(
            pages.Max(page => page.PreferredSize.Width), pages.Max(page => page.PreferredSize.Height));
        // The tab strip and border around the page, and a little room so nothing touches them.
        tabs.Size = largest + (tabs.Size - tabs.DisplayRectangle.Size) + new Size(8, 8);
        table.Controls.Add(tabs);
        table.Controls.Add(_error);
        table.Controls.Add(Flow(save, cancel));
        return table;
    }

    private static TabPage Page(string title, Control content)
    {
        // Scrolls when embedded in a main window shorter than the page.
        var page = new TabPage(title) { UseVisualStyleBackColor = true, AutoScroll = true };
        page.Controls.Add(content);
        return page;
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
        if (_capturing || !_actions.BeginCapture())
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
            _actions.CancelCapture();
            EndCapture();
        }
    }

    private void EndCapture()
    {
        _capturing = false;
        _changeShortcut.Enabled = true;
        _shortcutText.Text = _shortcut.Describe();
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task SaveAsync()
    {
        CancelCapture();
        var chosen = _current with
        {
            DictationShortcut = _shortcut,
            Microphone = _microphones[_microphone.SelectedIndex],
            HandsFree = _handsFree.Checked,
            TranscriptionProvider = (TranscriptionProvider)_provider.SelectedIndex,
            LocalModel = ChosenModel,
            TranscriptionTimeoutSeconds = (int)_timeout.Value,
        };
        if (chosen.TranscriptionProvider == TranscriptionProvider.Local && !LocalModel.For(ChosenModel).IsReady())
        {
            _error.Text = "Download the model first, or transcribe with the cloud service.";
            return;
        }

        if (!SettingsInput.TryBuild(chosen, _language.Text, _vocabulary.Text, out var settings, out var error)
            || !_cloud.TryRead(out var transcription, out error)
            || !_languageModel.TryApply(settings, out settings, out var languageModel, out error))
        {
            _error.Text = error;
            return;
        }

        settings = settings with
        {
            TranscriptionService = transcription.Service,
            TranscriptionBaseUrl = transcription.CustomUrl,
            TranscriptionModel = transcription.Model,
        };

        // A new key is checked first; one the service rejects is never stored.
        var newKeys = new Dictionary<CloudService, string>();
        foreach (var choice in new[] { transcription, languageModel })
        {
            if (choice.NewKey is { } key)
            {
                if (newKeys.TryGetValue(choice.Service, out var other) && other != key)
                {
                    _error.Text = $"Two different {CloudServices.For(choice.Service).Name} keys were typed. A " +
                        "service has one key: type it on one tab only.";
                    return;
                }

                newKeys[choice.Service] = key;
            }
        }

        _error.Text = newKeys.Count > 0 ? "Checking the new key..." : "";
        _saveButton.Enabled = false;
        try
        {
            foreach (var (service, key) in newKeys)
            {
                if (await RejectsAsync(service, key, settings) is { } rejected)
                {
                    _error.Text = rejected;
                    return;
                }
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _saveButton.Enabled = true;
            }
        }

        settings = _general.Apply(settings);
        if (!IsDisposed && _actions.Save(new(settings, newKeys, _general.StartAtLogin)) is { } problem)
        {
            _error.Text = problem;
            return;
        }

        if (_embedded)
        {
            _error.Text = "Saved.";
            return;
        }

        Close();
    }

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
            var result = await _actions.DownloadModel(LocalModel.For(choice), progress, _closing.Token);
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
    private async Task TestTranscriptionAsync()
    {
        _testTranscriptionButton.Enabled = false;
        _testTranscriptionResult.Text = "Sending a second of silence...";
        try
        {
            if (!SettingsInput.TryBuild(_current, _language.Text, _vocabulary.Text, out var settings, out var error)
                || !_cloud.TryRead(out var cloud, out error))
            {
                _testTranscriptionResult.Text = error;
                return;
            }

            settings = settings with
            {
                TranscriptionProvider = (TranscriptionProvider)_provider.SelectedIndex,
                LocalModel = ChosenModel,
                TranscriptionTimeoutSeconds = (int)_timeout.Value,
                TranscriptionService = cloud.Service,
                TranscriptionBaseUrl = cloud.CustomUrl,
                TranscriptionModel = cloud.Model,
            };
            _testTranscriptionResult.Text = SettingsInput.TranscriptionTestMessage(
                await _actions.TestTranscription(settings, cloud.NewKey, _closing.Token));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while the test ran.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.TranscriptionTestFailed, e);
            _testTranscriptionResult.Text = "The test couldn't run.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _testTranscriptionButton.Enabled = true;
            }
        }
    }
}
