using Velopack;
using Velopack.Sources;

namespace Naad.Updates;

/// <summary>
/// Finds and installs new versions of Naad from the repository's GitHub Releases, through
/// Velopack. Only an installed copy updates; a build run from source never checks. Nothing is
/// downloaded or installed until the user agrees, and the only data sent is GitHub's normal
/// request for the release list.
/// </summary>
internal sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/ayushgade06/naad";

    // GitHub answers in well under a second; this only stops a dead network from leaving the
    // check pending for minutes.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    // The installer is about 60 MB; a slow connection gets several minutes before giving up.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    private readonly UpdateManager _manager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    private UpdateInfo? _available;

    /// <summary>Whether this copy was installed by Naad's installer, so it can update itself.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>
    /// Asks GitHub for a newer release. Returns its version, such as "0.2.0", or null when this
    /// copy is current or isn't installed. <c>Failed</c> is true when GitHub couldn't be asked; the
    /// exception type is logged.
    /// </summary>
    public async Task<(bool Failed, string? Version)> CheckAsync()
    {
        if (!IsInstalled)
        {
            return (false, null);
        }

        try
        {
            _available = await _manager.CheckForUpdatesAsync().WaitAsync(CheckTimeout);
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateCheckFailed, e);
            return (true, null);
        }

        if (_available is null)
        {
            Log.Info(LogEvent.UpdateNotAvailable);
            return (false, null);
        }

        var version = _available.TargetFullRelease.Version;
        Log.Info(
            LogEvent.UpdateAvailable,
            ("major", version.Major),
            ("minor", version.Minor),
            ("patch", version.Patch));
        return (false, version.ToString());
    }

    /// <summary>
    /// Downloads the release found by <see cref="CheckAsync"/> and arranges for it to be applied
    /// as soon as Naad exits, then started again. Returns false if nothing is available or the
    /// download failed. The caller then exits Naad the normal way.
    /// </summary>
    public async Task<bool> DownloadAndApplyOnExitAsync()
    {
        if (_available is not { } update)
        {
            return false;
        }

        try
        {
            using var timeout = new CancellationTokenSource(DownloadTimeout);
            await _manager.DownloadUpdatesAsync(update, progress: null, timeout.Token);
            _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true);
            Log.Info(LogEvent.UpdateReadyToApply);
            return true;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
            return false;
        }
    }
}
