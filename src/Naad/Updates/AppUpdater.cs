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
    /// What's new in every release after this copy, up to the one found by <see cref="CheckAsync"/>;
    /// empty when the release has no notes or nothing was found.
    /// </summary>
    public string Notes => _available is not { } update || _manager.CurrentVersion is not { } current
        ? ""
        : ReleaseNotes.Since(
            update.TargetFullRelease.NotesMarkdown, new Version(current.Major, current.Minor, current.Patch));

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
    /// Downloads the release found by <see cref="CheckAsync"/>, reporting each percent. Returns
    /// false if nothing is available or the download failed; calling again retries. Nothing is
    /// applied until <see cref="ApplyOnExit"/>.
    /// </summary>
    /// <param name="progress">0 to 100, raised on a thread-pool thread.</param>
    /// <param name="cancellationToken">The user's Cancel.</param>
    /// <exception cref="OperationCanceledException">The user cancelled, even if the download had just
    /// finished.</exception>
    public async Task<bool> DownloadAsync(Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_available is not { } update)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            await _manager.DownloadUpdatesAsync(update, progress, timeout.Token);

            // Cancel wins over a download that finished as it was clicked.
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Info(LogEvent.UpdateDownloadCancelled);
            throw;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
            return false;
        }
    }

    /// <summary>
    /// Applies the downloaded release once Naad exits, then starts it again. Call right before
    /// exiting: Velopack's updater waits only a minute for Naad to quit.
    /// </summary>
    public void ApplyOnExit()
    {
        if (_available is { } update)
        {
            _manager.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: false, restart: true);
            Log.Info(LogEvent.UpdateReadyToApply);
        }
    }
}
