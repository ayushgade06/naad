using Naad.Tray;

namespace Naad.Updates;

/// <summary>
/// The update offer, opened from its notification: what's new in every release since this copy,
/// then Install with a progress bar and Cancel, and Retry when the download fails. Nothing is
/// downloaded until Install is clicked, and nothing installs during a dictation or meeting notes.
/// Create, use and close it on the UI thread.
/// </summary>
internal sealed class UpdateWindow : Form
{
    private readonly AppUpdater _updater;
    private readonly Func<bool> _isIdle;
    private readonly Action _exit;
    private readonly Icon _icon = TrayIcon.LoadIcon();
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _status = new() { AutoSize = true, Dock = DockStyle.Fill };
    private readonly Button _install = new() { Text = "Install and restart", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Visible = false };
    private readonly Button _close = new() { Text = "Not now", AutoSize = true };
    private CancellationTokenSource? _download;

    /// <param name="updater">Has found the release, and downloads it.</param>
    /// <param name="version">The release on offer, such as "2.0.1".</param>
    /// <param name="isIdle">Whether no dictation or meeting is running, so Naad may restart.</param>
    /// <param name="exit">Ends Naad the normal way; Velopack then applies the update.</param>
    public UpdateWindow(AppUpdater updater, string version, Func<bool> isIdle, Action exit)
    {
        Version = version;
        _updater = updater;
        _isIdle = isIdle;
        _exit = exit;
        Text = $"Naad {version} is available";
        Icon = _icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        Size = new Size(560, 460);
        MinimumSize = new Size(420, 320);

        var notes = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Text = updater.Notes is { Length: > 0 } text
                ? text.ReplaceLineEndings(Environment.NewLine)
                : "This release has no notes. See the releases page on GitHub for what changed.",
        };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
        };
        buttons.Controls.AddRange([_close, _cancel, _install]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(notes);
        layout.Controls.Add(_progress);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        _install.Click += (_, _) => _ = InstallAsync();
        _cancel.Click += (_, _) => _download?.Cancel();
        _close.Click += (_, _) => Close();
        FormClosed += (_, _) => _download?.Cancel();
    }

    /// <summary>The release this window offers.</summary>
    public string Version { get; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _download?.Dispose();
            _icon.Dispose();
        }

        base.Dispose(disposing);
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task InstallAsync()
    {
        if (!_isIdle())
        {
            _status.Text = "Naad updates once your dictation or meeting notes are finished. Click Install then.";
            return;
        }

        _download?.Dispose();
        _download = new CancellationTokenSource();
        Downloading(true);
        _status.Text = "Downloading...";
        try
        {
            var progress = new Progress<int>(percent =>
            {
                // A report queued before Cancel must not overwrite what Cancel said.
                if (!_cancel.Visible)
                {
                    return;
                }

                _progress.Value = Math.Clamp(percent, 0, 100);
                _status.Text = $"Downloading... {_progress.Value}%";
            });
            var ready = await _updater.DownloadAsync(((IProgress<int>)progress).Report, _download.Token);

            // Closed meanwhile: nothing is applied; the next offer downloads again.
            if (IsDisposed)
            {
                return;
            }

            if (!ready)
            {
                _status.Text = "The download failed. Check your internet connection and try again.";
                _install.Text = "Retry";
            }
            else if (!_isIdle())
            {
                // A dictation or meeting began during the download: it is never cut off.
                _status.Text = "Downloaded. Naad updates once your dictation or meeting notes are finished. " +
                    "Click Install then.";
            }
            else
            {
                _status.Text = "Restarting Naad...";
                _updater.ApplyOnExit();
                _exit();
                return;
            }
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            if (IsDisposed)
            {
                return;
            }

            _status.Text = "Cancelled. Nothing was installed.";
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
            if (IsDisposed)
            {
                return;
            }

            _status.Text = "The download failed.";
            _install.Text = "Retry";
        }

        Downloading(false);
    }

    private void Downloading(bool downloading)
    {
        _progress.Visible = downloading;
        _progress.Value = 0;
        _install.Visible = !downloading;
        _cancel.Visible = downloading;
    }
}
