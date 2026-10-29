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
    private readonly Func<AppSettings, string?, bool, string?> _save;
    private readonly Func<bool> _beginCapture;
    private readonly Action _cancelCapture;
    private readonly CancellationTokenSource _closing = new();
    private readonly Icon _icon = TrayIcon.LoadIcon();

    // Index 0 is the Windows default (null); the rest are device names.
    private readonly List<string?> _microphones = [null];

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
    /// <param name="microphones">The connected recording devices' names.</param>
    /// <param name="startsAtLogin">Whether Naad starts when the user signs in.</param>
    /// <param name="testKey">Checks a key against a base URL.</param>
    /// <param name="save">Saves and applies new settings, a new key if one was typed, and the
    /// start-at-login choice. Returns what went wrong, or null.</param>
    /// <param name="beginCapture">Starts capturing a shortcut; false if a dictation is running.</param>
    /// <param name="cancelCapture">Stops a capture early.</param>
    public SettingsWindow(
        AppSettings current,
        ApiKeyResult key,
        IReadOnlyList<string> microphones,
        bool startsAtLogin,
        Func<string, Uri, CancellationToken, Task<KeyTestResult>> testKey,
        Func<AppSettings, string?, bool, string?> save,
        Func<bool> beginCapture,
        Action cancelCapture)
    {
        _current = current;
        _key = key;
        _testKey = testKey;
        _save = save;
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
        Controls.Add(BuildLayout());

        _testKeyButton.Click += (_, _) => _ = TestKeyAsync();
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
        _shortcutText.Text = _shortcut.Describe();
        _language.Text = _current.Language ?? "";
        _vocabulary.Text = _current.Vocabulary ?? "";
        _handsFree.Checked = _current.HandsFree;
        _baseUrl.Text = _current.TranscriptionBaseUrl.AbsoluteUri;
        _model.Text = _current.TranscriptionModel;
        _playSounds.Checked = _current.PlaySounds;
        _fileLogging.Checked = _current.FileLogging;
    }

    private TableLayoutPanel BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Save();
        AcceptButton = save;
        CancelButton = cancel;

        Row(table, "API key", Note(SettingsInput.KeyStatus(_key)));
        Row(table, "New key", Flow(_newKey, _testKeyButton));
        Row(table, "", _testResult);
        Row(table, "Shortcut", Flow(_shortcutText, _changeShortcut));
        Row(table, "", _handsFree);
        Row(table, "Microphone", _microphone);
        Row(table, "Language", Flow(_language, Note("Code such as en or hi; blank detects it.")));
        Row(table, "Vocabulary", _vocabulary);
        Row(table, "", Note("Names and terms you use, separated by commas. Sent with each recording."));
        Row(table, "Base URL", _baseUrl);
        Row(table, "Model", _model);
        Row(table, "", _playSounds);
        Row(table, "", _fileLogging);
        Row(table, "", _startAtLogin);
        Row(table, "", _error);
        Row(table, "", Flow(save, cancel));
        return table;
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
        };
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

        var newKey = _newKey.Text.Trim() is { Length: > 0 } typed ? typed : null;
        if (_save(settings, newKey, _startAtLogin.Checked) is { } problem)
        {
            _error.Text = problem;
            return;
        }

        Close();
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
