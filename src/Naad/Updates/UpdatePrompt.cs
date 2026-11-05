using Naad.Tray;

namespace Naad.Updates;

/// <summary>
/// Offers updates in the tray: a minute after start and once a day while automatic checks are on,
/// and from Check for updates. The automatic check offers a release only three days after it was
/// published, and never one the user skipped (<see cref="UpdatePolicy"/>); Check for updates warns
/// about a newer one first. Only releases signed by Naad's publisher are offered. An update is
/// only downloaded when the user asks, and never during a dictation. Does nothing in a copy that
/// wasn't installed. Create, use and dispose it on the UI thread.
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
    private readonly Func<bool> _automatic;
    private readonly Action _exit;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = FirstCheckMilliseconds };
    private UpdateWindow? _window;
    private Task? _checking;
    private bool _manualPending;

    // The release offered in this session, for the overlay's reminder; null when none is.
    private FoundRelease? _offered;

    /// <param name="updater">Talks to GitHub Releases.</param>
    /// <param name="tray">Shows the menu item and the notifications.</param>
    /// <param name="isIdle">Whether no dictation or meeting is running, so Naad may restart.</param>
    /// <param name="automatic">Whether the automatic checks are on, read at each one.</param>
    /// <param name="exit">Ends Naad the normal way; Velopack then applies the update.</param>
    public UpdatePrompt(AppUpdater updater, TrayIcon tray, Func<bool> isIdle, Func<bool> automatic, Action exit)
    {
        _updater = updater;
        _tray = tray;
        _isIdle = isIdle;
        _automatic = automatic;
        _exit = exit;
        if (!updater.IsInstalled)
        {
            return;
        }

        tray.EnableUpdateChecks(() => CheckAsync(manual: true));
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = DailyCheckMilliseconds;
            if (_automatic())
            {
                _ = CheckAsync(manual: false);
            }
        };
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _window?.Dispose();
    }

    // One check at a time, so two never write the state or offer different releases at once. A
    // click during the daily check makes that check answer as a click would.
    private Task CheckAsync(bool manual)
    {
        _manualPending |= manual;
        if (_checking is { IsCompleted: false } running)
        {
            return running;
        }

        return _checking = RunCheckAsync();
    }

    // Fire-and-forget from a timer or a click: anything not caught here would vanish silently.
    private async Task RunCheckAsync()
    {
        try
        {
            var check = await _updater.CheckAsync();

            // Read after the check: a click while it ran counts.
            var manual = _manualPending;
            _manualPending = false;
            var now = DateTimeOffset.UtcNow;
            var state = UpdateState.Load(UpdateState.DefaultPath);
            if (!check.Failed)
            {
                state = state with { LastChecked = now };
                state.Save(UpdateState.DefaultPath);
            }

            if (check.Found is not { } found)
            {
                // Nothing (or nothing trustworthy) on offer any more.
                _offered = null;
                _tray.ShowUpdateOffer(null, null, null);
                if (manual)
                {
                    Tell(check);
                }

                return;
            }

            if (!UpdatePolicy.Offer(state, found.Version, found.Published, now, manual))
            {
                _offered = null;
                _tray.ShowUpdateOffer(null, null, null);
                Log.Info(LogEvent.UpdateWaiting);
                return;
            }

            _tray.ShowUpdateOffer(found.Version, () => OpenWindow(found, install: false), () =>
            {
                var skipped = UpdateState.Load(UpdateState.DefaultPath) with { Skipped = found.Version };
                skipped.Save(UpdateState.DefaultPath);
                _tray.ShowUpdateOffer(null, null, null);
                _offered = null;
                Log.Info(LogEvent.UpdateSkipped);
            });
            if (manual && UpdatePolicy.IsRecent(found.Published, now))
            {
                WarnRecent(found, now);
            }
            else
            {
                _offered = found;
                _tray.ShowUpdateAvailable(found.Version, install: () => OpenWindow(found, install: false));
            }
        }
        catch (Exception e)
        {
            _manualPending = false;
            Log.Error(LogEvent.UpdateCheckFailed, e);
        }
    }

    private void Tell(UpdateCheck check)
    {
        if (check.Refused)
        {
            _tray.ShowUpdateRefused();
        }
        else if (check.Failed)
        {
            _tray.ShowUpdateFailed();
        }
        else
        {
            _tray.ShowUpToDate();
        }
    }

    // A release in its first days, found by Check for updates: the user decides with the age in view.
    private void WarnRecent(FoundRelease found, DateTimeOffset now)
    {
        var download = new TaskDialogButton("Download now");
        var notes = new TaskDialogButton("What's new");
        var wait = new TaskDialogButton("Wait");
        var age = UpdatePolicy.Age(now - found.Published);
        var page = new TaskDialogPage
        {
            Caption = "Naad update",
            Heading = $"Naad {found.Version} was released {age}",
            Text = "It's very new. You can download it now, or wait a few days in case a problem turns up; Naad " +
                "offers it again by itself then.",
            Icon = TaskDialogIcon.Information,
            Buttons = { download, notes, wait },
            DefaultButton = wait,
        };
        // In the middle of the screen: a tray app has no window for it to open over.
        var chosen = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);
        if (chosen == download || chosen == notes)
        {
            // Offered from now on; Wait leaves it to the automatic check, with no reminder meanwhile.
            _offered = found;
            OpenWindow(found, install: chosen == download);
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
        if (!pasted || _offered is not { } found)
        {
            return;
        }

        var state = UpdateState.Load(UpdateState.DefaultPath);
        if (!UpdatePolicy.Mark(state, found.Version))
        {
            return;
        }

        (state with { Marked = found.Version }).Save(UpdateState.DefaultPath);
        Log.Info(LogEvent.UpdateMarked);
        show("Update available", () => OpenWindow(found, install: false));
    }

    // Only from the user's choice: the window shows what's new, and downloads only on Install (or
    // at once for Download now).
    private void OpenWindow(FoundRelease found, bool install)
    {
        if (_window is { IsDisposed: false } open && open.Release == found)
        {
            open.Activate();
        }
        else
        {
            // A newer release was found since: its window replaces the stale one.
            _window?.Close();
            _window = new UpdateWindow(_updater, found, _isIdle, _exit);
            _window.FormClosed += (_, _) => _window = null;
            _window.Show();
        }

        if (install)
        {
            _window.Install();
        }
    }
}
