using Naad.Tray;

namespace Naad.Updates;

/// <summary>
/// Offers updates in the tray: a minute after start, once a day, and from Check for updates.
/// The daily check offers a release only three days after Naad first sees it, and never one the
/// user skipped (<see cref="UpdatePolicy"/>). An update is only downloaded when the user clicks
/// its notification, and never during a dictation. Does nothing in a copy that wasn't
/// installed. Clicking the offer opens <see cref="UpdateWindow"/>. Create, use and dispose it on
/// the UI thread.
/// </summary>
internal sealed class UpdatePrompt : IDisposable
{
    // A minute after start: the network is usually up by then, and starting stays quick.
    private const int FirstCheckMilliseconds = 60_000;

    // Once a day is plenty for a desktop app's releases.
    private const int DailyCheckMilliseconds = 24 * 60 * 60 * 1000;

    private readonly AppUpdater _updater;
    private readonly TrayIcon _tray;
    private readonly Func<bool> _isIdle;
    private readonly Action _exit;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = FirstCheckMilliseconds };
    private UpdateWindow? _window;

    // The version offered in this session, for the overlay's reminder; null when none is.
    private string? _offered;

    /// <param name="updater">Talks to GitHub Releases.</param>
    /// <param name="tray">Shows the menu item and the notifications.</param>
    /// <param name="isIdle">Whether no dictation or meeting is running, so Naad may restart.</param>
    /// <param name="exit">Ends Naad the normal way; Velopack then applies the update.</param>
    public UpdatePrompt(AppUpdater updater, TrayIcon tray, Func<bool> isIdle, Action exit)
    {
        _updater = updater;
        _tray = tray;
        _isIdle = isIdle;
        _exit = exit;
        if (!updater.IsInstalled)
        {
            return;
        }

        tray.EnableUpdateChecks(() => _ = CheckAsync(manual: true));
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = DailyCheckMilliseconds;
            _ = CheckAsync(manual: false);
        };
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _window?.Dispose();
    }

    // Fire-and-forget from a timer or a click: anything not caught here would vanish silently.
    private async Task CheckAsync(bool manual)
    {
        try
        {
            var (failed, version) = await _updater.CheckAsync();
            if (version is not null)
            {
                var now = DateTimeOffset.UtcNow;
                var state = UpdatePolicy.Saw(UpdateState.Load(UpdateState.DefaultPath), version, now);
                state.Save(UpdateState.DefaultPath);
                if (!UpdatePolicy.Offer(state, version, now, manual))
                {
                    // An older version's Skip item would be stale.
                    _tray.ShowSkipUpdate(null, null);
                    Log.Info(LogEvent.UpdateWaiting);
                    return;
                }

                _offered = version;
                _tray.ShowUpdateAvailable(version, install: () => OpenWindow(version));
                _tray.ShowSkipUpdate(version, () =>
                {
                    var skipped = UpdateState.Load(UpdateState.DefaultPath) with { Skipped = version };
                    skipped.Save(UpdateState.DefaultPath);
                    _tray.ShowSkipUpdate(null, null);
                    _offered = null;
                    Log.Info(LogEvent.UpdateSkipped);
                });
            }
            else if (manual && failed)
            {
                _tray.ShowUpdateFailed();
            }
            else if (manual)
            {
                _tray.ShowUpToDate();
            }
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateCheckFailed, e);
        }
    }

    /// <summary>
    /// After a dictation: says "Update available" on the overlay once per offered version, so the
    /// offer isn't missed when the notification was. Call on the UI thread.
    /// </summary>
    /// <param name="pasted">The dictation was pasted and the overlay shows no other notice, which
    /// the reminder must not cover.</param>
    /// <param name="show">Shows a short notice on the overlay; clicking it calls the action.</param>
    public void MarkAfterDictation(bool pasted, Action<string, Action?> show)
    {
        if (!pasted || _offered is null)
        {
            return;
        }

        var state = UpdateState.Load(UpdateState.DefaultPath);
        if (!UpdatePolicy.Mark(state, _offered))
        {
            return;
        }

        (state with { Marked = _offered }).Save(UpdateState.DefaultPath);
        Log.Info(LogEvent.UpdateMarked);
        var version = _offered;
        show("Update available", () => OpenWindow(version));
    }

    // Only from the user's click: the window shows what's new, and downloads only on Install.
    private void OpenWindow(string version)
    {
        if (_window is { IsDisposed: false } open && open.Version == version)
        {
            open.Activate();
            return;
        }

        // A newer release was found since: its window replaces the stale one.
        _window?.Close();

        _window = new UpdateWindow(_updater, version, _isIdle, _exit);
        _window.FormClosed += (_, _) => _window = null;
        _window.Show();
    }
}
