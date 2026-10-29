using Naad.Tray;

namespace Naad.Updates;

/// <summary>
/// Offers updates in the tray: a minute after start, once a day, and from Check for updates.
/// An update is only downloaded when the user clicks its notification, and never during a
/// dictation. Does nothing in a copy that wasn't installed. Create, use and dispose it on the
/// UI thread.
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

    /// <param name="updater">Talks to GitHub Releases.</param>
    /// <param name="tray">Shows the menu item and the notifications.</param>
    /// <param name="isIdle">Whether no dictation is running, so Naad may restart.</param>
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

    public void Dispose() => _timer.Dispose();

    // Fire-and-forget from a timer or a click: anything not caught here would vanish silently.
    private async Task CheckAsync(bool manual)
    {
        try
        {
            var (failed, version) = await _updater.CheckAsync();
            if (version is not null)
            {
                _tray.ShowUpdateAvailable(version, install: () => _ = InstallAsync());
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

    // Runs only from the user's click. A dictation in progress is never cut off by a restart.
    private async Task InstallAsync()
    {
        try
        {
            if (!_isIdle())
            {
                _tray.ShowUpdateWaits(install: () => _ = InstallAsync());
                return;
            }

            if (await _updater.DownloadAndApplyOnExitAsync())
            {
                _exit();
            }
            else
            {
                _tray.ShowUpdateFailed();
            }
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
        }
    }
}
