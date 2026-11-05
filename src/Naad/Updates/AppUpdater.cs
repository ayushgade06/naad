using System.Net;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Naad.Updates;

/// <summary>A release a check found, with its signed release list.</summary>
/// <param name="Update">What Velopack found.</param>
/// <param name="Manifest">The release list, already checked against Naad's public key.</param>
internal sealed record FoundRelease(UpdateInfo Update, ReleaseManifest Manifest)
{
    public string Version => Manifest.Version;

    public DateTimeOffset Published => Manifest.Published;
}

/// <param name="Failed">GitHub couldn't be asked.</param>
/// <param name="Found">A newer release, signed by Naad's publisher; null for none.</param>
/// <param name="Refused">A newer release was found but isn't signed by Naad's publisher.</param>
internal readonly record struct UpdateCheck(bool Failed, FoundRelease? Found, bool Refused = false);

/// <summary>How a download ended.</summary>
internal enum DownloadResult
{
    Ready,
    Failed,

    /// <summary>The file isn't the one the signed release list names: it is deleted, never installed.</summary>
    Refused,
}

/// <summary>
/// Finds and installs new versions of Naad from the repository's GitHub Releases, through
/// Velopack. Only an installed copy updates; a build run from source never checks. A release is
/// offered only when its signed release list checks out against the public key built in here, and
/// the downloaded package is checked against that list before it is applied. Nothing is downloaded
/// or installed until the user agrees, and the only data sent is GitHub's normal requests for the
/// release and its list.
/// </summary>
internal sealed class AppUpdater
{
    private const string RepositoryUrl = "https://github.com/ayushgade06/naad";

    // Naad's release-signing keys (ECDSA P-256): a release signed by any of them is trusted. The
    // private half is the repository's NAAD_UPDATE_SIGNING_KEY secret. Rotating means shipping
    // the new key here beside the old one first (docs/releasing.md).
    private static readonly string[] PublicKeys =
    [
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAECqWGvlaLnDPeMH+CHGrz+6aRtUSq
        uqCOGzEu02CwRnCLOVr1WUk83yxQp41EbcBeQJyizCmaSykTRDcHZIa0rw==
        -----END PUBLIC KEY-----
        """,
    ];

    // GitHub answers in well under a second; this only stops a dead network from leaving the
    // check pending for minutes.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    // The installer is about 60 MB; a slow connection gets several minutes before giving up.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);

    // The release list is a few hundred bytes from the same host as the release itself.
    private static readonly HttpClient Http = new() { Timeout = CheckTimeout };

    private readonly UpdateManager _manager =
        new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));

    /// <summary>Whether this copy was installed by Naad's installer, so it can update itself.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>
    /// What's new in every release after this copy, up to <paramref name="found"/>; empty when the
    /// release has no notes.
    /// </summary>
    public string Notes(FoundRelease found) => _manager.CurrentVersion is not { } current
        ? ""
        : ReleaseNotes.Since(
            found.Update.TargetFullRelease.NotesMarkdown, new Version(current.Major, current.Minor, current.Patch));

    /// <summary>
    /// Asks GitHub for a newer release and its signed release list. Nothing found when this copy is
    /// current or isn't installed. The exception type of a failure is logged.
    /// </summary>
    public async Task<UpdateCheck> CheckAsync()
    {
        if (!IsInstalled)
        {
            return new(false, null);
        }

        UpdateInfo? update;
        try
        {
            update = await _manager.CheckForUpdatesAsync().WaitAsync(CheckTimeout);
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateCheckFailed, e);
            return new(true, null);
        }

        if (update is null)
        {
            Log.Info(LogEvent.UpdateNotAvailable);
            return new(false, null);
        }

        var asset = update.TargetFullRelease;
        var version = asset.Version.ToString();
        ReleaseManifest? manifest;
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            var folder = $"{RepositoryUrl}/releases/download/v{version}/";
            var list = await Http.GetByteArrayAsync(folder + ReleaseManifest.FileName, timeout.Token);
            var signature = await Http.GetStringAsync(folder + ReleaseManifest.SignatureFileName, timeout.Token);
            manifest = PublicKeys.Select(key => ReleaseManifest.Verify(list, signature, key))
                .FirstOrDefault(m => m is not null);
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            // A release without a list wasn't made by Naad's release workflow.
            manifest = null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Log.Error(LogEvent.UpdateCheckFailed, e);
            return new(true, null);
        }

        if (manifest is null || !manifest.Covers(asset.PackageId, version, asset.FileName, asset.Size, asset.SHA256))
        {
            Log.Warn(LogEvent.UpdateRefused);
            return new(false, null, Refused: true);
        }

        Log.Info(
            LogEvent.UpdateAvailable,
            ("major", asset.Version.Major),
            ("minor", asset.Version.Minor),
            ("patch", asset.Version.Patch));
        return new(false, new(update, manifest));
    }

    /// <summary>
    /// Downloads <paramref name="found"/>, reporting each percent, then checks the package against
    /// its signed release list. Calling again retries. Nothing is applied until <see cref="ApplyOnExit"/>.
    /// </summary>
    /// <param name="found">From <see cref="CheckAsync"/>.</param>
    /// <param name="progress">0 to 100, raised on a thread-pool thread.</param>
    /// <param name="cancellationToken">The user's Cancel.</param>
    /// <exception cref="OperationCanceledException">The user cancelled, even if the download had just
    /// finished.</exception>
    public async Task<DownloadResult> DownloadAsync(
        FoundRelease found, Action<int>? progress = null, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        try
        {
            await _manager.DownloadUpdatesAsync(found.Update, progress, timeout.Token);

            // Cancel wins over a download that finished as it was clicked.
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A download that finished as Cancel was clicked is never left to be applied later.
            Log.Info(LogEvent.UpdateDownloadCancelled);
            Discard(found);
            throw;
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
            return DownloadResult.Failed;
        }

        return await Task.Run(() => Check(found));
    }

    /// <summary>
    /// Applies <paramref name="found"/>, downloaded and checked, once Naad exits, then starts it
    /// again. Call right before exiting: Velopack's updater waits only a minute for Naad to quit.
    /// </summary>
    public void ApplyOnExit(FoundRelease found)
    {
        _manager.WaitExitThenApplyUpdates(found.Update.TargetFullRelease, silent: false, restart: true);
        Log.Info(LogEvent.UpdateReadyToApply);
    }

    // Best effort: start never applies a package by itself (SetAutoApplyOnStartup(false)), and the
    // next download replaces this one.
    private static void Discard(FoundRelease found)
    {
        try
        {
            File.Delete(Path.Combine(VelopackLocator.Current.PackagesDir!, found.Update.TargetFullRelease.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
        }
    }

    // The package on disk is the one the signed list names, byte for byte; anything else is deleted.
    private static DownloadResult Check(FoundRelease found)
    {
        var asset = found.Update.TargetFullRelease;
        var path = Path.Combine(VelopackLocator.Current.PackagesDir!, asset.FileName);
        try
        {
            bool matches;
            using (var file = File.OpenRead(path))
            {
                matches = found.Manifest.Matches(asset.FileName, file);
            }

            if (matches)
            {
                return DownloadResult.Ready;
            }

            Log.Warn(LogEvent.UpdateRefused);
            Discard(found);
            return DownloadResult.Refused;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.UpdateDownloadFailed, e);
            return DownloadResult.Failed;
        }
    }
}
