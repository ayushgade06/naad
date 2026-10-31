using Naad.Cleanup;
using Naad.Tray;

namespace Naad.Settings;

/// <summary>
/// Edits the cleanup instructions, with Reset to default, a notice when a newer default has
/// shipped, and a test on editable sample text that shows the reply, its time and the full
/// prompt sent. Modal; create and use it on the UI thread.
/// </summary>
internal sealed class CleanupPromptDialog : Form
{
    /// <summary>Invented text with hesitations, a stutter and a self-correction to tidy.</summary>
    public const string Sample = "um so I think we should uh meet on tuesday at three no wait four pm to talk " +
        "about the the budget";

    private const int TextWidth = 560;

    private readonly Func<string?, string, CancellationToken, Task<PromptTest>> _test;
    private readonly CancellationTokenSource _closing = new();
    private readonly Icon _icon = TrayIcon.LoadIcon();
    private readonly TextBox _prompt = Box(lines: 14);
    private readonly TextBox _sample = Box(lines: 3);
    private readonly TextBox _reply = Box(lines: 4, readOnly: true);
    private readonly TextBox _sent = Box(lines: 6, readOnly: true);
    private readonly Button _testButton = new() { Text = "Test", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(TextWidth, 0) };
    private readonly string? _originalCustom;
    private readonly string? _originalVersion;

    /// <param name="custom">The custom instructions in use, or null for the default.</param>
    /// <param name="version">The default version they were based on.</param>
    /// <param name="test">Sends instructions (null for the default) and a sample to the saved model.</param>
    public CleanupPromptDialog(
        string? custom, string? version, Func<string?, string, CancellationToken, Task<PromptTest>> test)
    {
        _test = test;
        _originalCustom = custom;
        _originalVersion = version;
        Custom = custom;
        Version = version;

        Text = "Cleanup prompt";
        Icon = _icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _prompt.Text = (custom ?? CleanupPrompt.DefaultInstructions).ReplaceLineEndings("\r\n");
        _sample.Text = Sample;

        var reset = new Button { Text = "Reset to default", AutoSize = true };
        reset.Click += (_, _) => _prompt.Text = CleanupPrompt.DefaultInstructions.ReplaceLineEndings("\r\n");
        _testButton.Click += (_, _) => _ = TestAsync();
        _sample.TextChanged += (_, _) => _testButton.Enabled = _sample.Text.Trim().Length > 0;
        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();
        CancelButton = cancel;

        // Ctrl+Enter keeps the edit, as Enter is a new line in the prompt.
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Accept();
            }
        };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12),
        };
        if (custom is not null && string.CompareOrdinal(version ?? "", CleanupPrompt.DefaultVersion) < 0)
        {
            var switchToDefault = new Button { Text = "Switch to default", AutoSize = true };
            switchToDefault.Click += (_, _) => reset.PerformClick();
            layout.Controls.Add(new Label
            {
                Text = "A newer default prompt is available. Your own prompt is kept until you switch and click OK.",
                AutoSize = true,
                MaximumSize = new Size(TextWidth, 0),
            });
            layout.Controls.Add(switchToDefault);
        }

        layout.Controls.Add(new Label { Text = "Instructions for the language model", AutoSize = true });
        layout.Controls.Add(_prompt);
        layout.Controls.Add(reset);
        layout.Controls.Add(new Label { Text = "Sample text (uses the saved AI settings)", AutoSize = true });
        layout.Controls.Add(_sample);
        layout.Controls.Add(_testButton);
        layout.Controls.Add(_status);
        layout.Controls.Add(new Label { Text = "Reply", AutoSize = true });
        layout.Controls.Add(_reply);
        layout.Controls.Add(new Label { Text = "Full prompt sent", AutoSize = true });
        layout.Controls.Add(_sent);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        FormClosed += (_, _) => _closing.Cancel();
    }

    /// <summary>The instructions chosen: null for the default.</summary>
    public string? Custom { get; private set; }

    /// <summary>The default version <see cref="Custom"/> is based on, or null.</summary>
    public string? Version { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closing.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    private static TextBox Box(int lines, bool readOnly = false) => new()
    {
        Multiline = true,
        ReadOnly = readOnly,
        AcceptsReturn = true,
        ScrollBars = ScrollBars.Vertical,
        Width = TextWidth,
        Height = (lines * 16) + 8,
    };

    // The typed instructions, or null when they are blank or the default.
    private string? Typed()
    {
        var text = _prompt.Text.ReplaceLineEndings("\n").Trim();
        return text.Length == 0 || text == CleanupPrompt.DefaultInstructions.Trim() ? null : text;
    }

    private void Accept()
    {
        var typed = Typed();
        if (typed is not null && !AppSettings.IsValidCleanupPrompt(typed))
        {
            _status.Text = "The prompt must be at most 8,000 characters.";
            return;
        }

        Custom = typed;

        // Unchanged text keeps the version it was written against, so the notice stays honest.
        Version = typed is null ? null
            : typed == _originalCustom?.ReplaceLineEndings("\n").Trim() ? _originalVersion
            : CleanupPrompt.DefaultVersion;
        DialogResult = DialogResult.OK;
        Close();
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task TestAsync()
    {
        _testButton.Enabled = false;
        _status.Text = "Waiting for the model... The first run on this PC can take up to a minute.";
        try
        {
            var result = await _test(Typed(), _sample.Text.Trim(), _closing.Token);
            _sent.Text = $"{result.Sent.Instructions}\n\n---\n\n{result.Sent.Text}".ReplaceLineEndings("\r\n");
            _reply.Text = result.Reply.ReplaceLineEndings("\r\n");
            _status.Text = SettingsInput.PromptTestMessage(result);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The dialog closed while the test ran.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.CleanupPromptTested, e);
            _status.Text = "The test couldn't run.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _testButton.Enabled = true;
            }
        }
    }
}
