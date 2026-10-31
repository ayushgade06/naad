using System.Diagnostics;
using System.Drawing.Drawing2D;
using Naad.Audio;
using Naad.Dictation;
using Naad.Hotkey;
using Naad.Native;
using Naad.Transcription;

namespace Naad.Tray;

/// <summary>Naad's notification-area icon and menu. Create and dispose it on the UI thread.</summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Title of the notification shown when a dictation can't record.</summary>
    public const string MicrophoneErrorTitle = "Naad can't use the microphone";

    /// <summary>Title of the notification shown when a transcript is kept instead of inserted.</summary>
    public const string TranscriptKeptTitle = "Naad kept your text";

    /// <summary>The menu item that puts a kept transcript on the clipboard.</summary>
    public const string CopyLastTranscriptText = "Copy last transcript";

    /// <summary>The menu item that opens the settings window.</summary>
    public const string SettingsText = "Settings...";

    /// <summary>The menu item that checks GitHub for a newer version; only in an installed copy.</summary>
    public const string CheckForUpdatesText = "Check for updates";

    /// <summary>Title of the notifications about updates.</summary>
    public const string UpdateTitle = "Naad update";

    private const string UpToDateMessage = "Naad is up to date.";

    private const string UpdateFailedMessage = "Naad couldn't check for or download the update. Check your " +
        "internet connection and try again from the Naad menu.";

    private const string UpdateWaitsMessage = "Naad will update once your dictation is finished. Click the " +
        "notification again then.";

    /// <summary>Title of the notification shown when the shortcut is pressed before setup is finished.</summary>
    public const string ApiKeyNeededTitle = "Naad isn't set up yet";

    /// <summary>What that notification says; clicking it opens Settings.</summary>
    public const string ApiKeyNeededMessage = "Click here to open Settings and download the model to " +
        "transcribe on this PC, or add an API key for the cloud. Nothing was recorded.";

    /// <summary>Title of the notification shown when a recording couldn't be transcribed.</summary>
    public const string TranscriptionErrorTitle = "Naad couldn't transcribe";

    private const string CopyFailedMessage = "The clipboard is busy. Try Copy last transcript again.";

    private const string NothingHeardMessage = "Naad didn't hear anything. Check that your microphone is " +
        "on, not muted and close enough, then try again.";

    // State dots drawn over the icon: red while listening, amber while transcribing. Both read
    // against light and dark taskbars thanks to a white ring.
    private static readonly Color RecordingColor = Color.FromArgb(0xD3, 0x2F, 0x2F);
    private static readonly Color TranscribingColor = Color.FromArgb(0xF9, 0xA8, 0x25);

    // Windows' microphone privacy page; the URI is documented as ms-settings:privacy-microphone.
    private const string MicrophonePrivacySettings = "ms-settings:privacy-microphone";

    // Windows 11 shows balloon tips as toasts and ignores the timeout; Windows 10 uses it.
    private const int NotificationMilliseconds = 10_000;

    private readonly Icon _image = LoadIcon();
    private readonly Icon _recordingImage;
    private readonly Icon _transcribingImage;
    private string _idleTooltip;
    private string _recordingTooltip;
    private readonly ContextMenuStrip _menu = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _copyLastTranscript;
    private readonly ToolStripMenuItem _checkForUpdates = new(CheckForUpdatesText) { Visible = false };
    private readonly Func<Task<bool>> _copy;
    private readonly Action _openSettings;
    private Action? _notificationClicked;

    /// <param name="shortcut">Named in the tooltip.</param>
    /// <param name="handsFree">Whether the shortcut is pressed to start and stop, rather than held.</param>
    /// <param name="copyLastTranscript">Copies the kept transcript; false if it couldn't.</param>
    /// <param name="openSettings">Opens the settings window.</param>
    /// <param name="exit">Ends the app.</param>
    public TrayIcon(
        Chord shortcut, bool handsFree, Func<Task<bool>> copyLastTranscript, Action openSettings, Action exit)
    {
        _copy = copyLastTranscript;
        _openSettings = openSettings;
        _idleTooltip = Tooltip(shortcut, handsFree);
        _recordingTooltip = RecordingTooltip(shortcut, handsFree);
        _recordingImage = WithDot(_image, RecordingColor);
        _transcribingImage = WithDot(_image, TranscribingColor);
        _copyLastTranscript = new ToolStripMenuItem(CopyLastTranscriptText, null, (_, _) => _ = CopyAsync())
        {
            Enabled = false,
        };
        _menu.Items.Add(SettingsText, null, (_, _) => openSettings());
        _menu.Items.Add(_copyLastTranscript);
        _menu.Items.Add(_checkForUpdates);
        _menu.Items.Add("Exit", null, (_, _) => exit());
        _icon = new NotifyIcon { Icon = _image, Text = _idleTooltip, ContextMenuStrip = _menu, Visible = true };
        _icon.BalloonTipClicked += (_, _) => _notificationClicked?.Invoke();
    }

    /// <summary>
    /// Also the icon's accessible name, which UI tests use to find it. NotifyIcon refuses
    /// text over 127 characters; a chord's four-key limit keeps it well under that.
    /// </summary>
    public static string Tooltip(Chord shortcut, bool handsFree = false) => handsFree
        ? $"Naad: press {shortcut.Describe()} to start and stop dictating"
        : $"Naad: hold {shortcut.Describe()} to dictate";

    /// <summary>The tooltip while listening: how to end the dictation.</summary>
    public static string RecordingTooltip(Chord shortcut, bool handsFree) => handsFree
        ? $"Naad: listening. Press {shortcut.Describe()} again to transcribe."
        : "Naad: listening. Release to transcribe.";

    /// <summary>Asks before updating. Windows cuts notification text at 255 characters.</summary>
    public static string UpdateAvailableMessage(string version) =>
        $"Naad {version} is available. Click here to download and install it; Naad restarts by itself.";

    /// <summary>What to tell the user. Windows cuts notification text at 255 characters.</summary>
    public static string MicrophoneErrorMessage(MicrophoneError error) => error switch
    {
        MicrophoneError.Blocked => "Windows privacy settings block the microphone. Click here, then turn on " +
            "Microphone access and Let desktop apps access your microphone.",
        MicrophoneError.NotFound => "No microphone was found. Connect one, or pick an input device in " +
            "Settings > System > Sound.",
        MicrophoneError.InUse => "Another app is using the microphone. Close it, then try again.",
        _ => "The microphone could not be opened. Check it in Settings > System > Sound, then try again.",
    };

    /// <summary>The tooltip while dictating; the idle tooltip otherwise.</summary>
    public static string StateTooltip(DictationState state, string idleTooltip, string recordingTooltip) => state switch
    {
        DictationState.Recording => recordingTooltip,
        DictationState.Transcribing or DictationState.Inserting => "Naad: transcribing",
        _ => idleTooltip,
    };

    /// <summary>What to tell the user when transcription fails. Never includes a response body.</summary>
    public static string TranscriptionErrorMessage(TranscriptionError error) => error switch
    {
        TranscriptionError.Unauthorized => "The API key was rejected. Open Settings from the Naad menu to " +
            "enter a valid key.",
        TranscriptionError.RateLimited => "The transcription service's rate limit was reached. Wait a " +
            "moment, then try again.",
        TranscriptionError.TooLarge => "The recording was too large for the transcription service. Try a " +
            "shorter dictation.",
        TranscriptionError.ServerError => "The transcription service had a problem. Try again in a moment.",
        TranscriptionError.Rejected => "The transcription service refused the request. Check the base URL " +
            "and model in Settings.",
        TranscriptionError.Network => "Naad couldn't reach the transcription service. Check your internet " +
            "connection.",
        TranscriptionError.NoInternet => "No internet connection. Connect and try again, or download the " +
            "model in Settings to transcribe on this PC when offline.",
        TranscriptionError.ConnectionLost => "The connection was lost while transcribing. Try again.",
        TranscriptionError.Timeout => "The request timed out: the transcription service didn't answer in time. " +
            "Try again.",
        TranscriptionError.LocalFailed => "Transcribing on this PC failed. Download the model again in Settings, " +
            "or switch to the cloud service.",
        _ => "The transcription service sent an unexpected answer. Check the base URL in Settings.",
    };

    /// <summary>What to tell the user when a transcript is kept. Windows cuts it at 255 characters.</summary>
    public static string TranscriptKeptMessage(TranscriptKeptReason reason) => reason switch
    {
        TranscriptKeptReason.WindowChanged => "The window changed before your text was ready, so Naad didn't " +
            "paste it. Click here or choose Copy last transcript in the Naad menu, then paste it with Ctrl+V.",
        TranscriptKeptReason.TargetElevated => "Windows doesn't let Naad type into apps running as " +
            "administrator. Click here or choose Copy last transcript in the Naad menu, then paste with Ctrl+V.",
        _ => "Naad couldn't paste your text. Click here or choose Copy last transcript in the Naad menu, " +
            "then paste it with Ctrl+V.",
    };

    /// <summary>Loads the embedded icon at the size Windows uses in the notification area.</summary>
    public static Icon LoadIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("Naad.ico")
            ?? throw new InvalidOperationException("The embedded Naad.ico resource is missing.");
        return new Icon(stream, SystemInformation.SmallIconSize);
    }

    /// <summary>
    /// Tells the user why a dictation couldn't record. For a privacy block, clicking the
    /// notification opens the microphone privacy settings.
    /// </summary>
    public void ShowMicrophoneError(MicrophoneError error)
    {
        _notificationClicked = error == MicrophoneError.Blocked ? OpenPrivacySettings : null;
        _icon.ShowBalloonTip(
            NotificationMilliseconds, MicrophoneErrorTitle, MicrophoneErrorMessage(error), ToolTipIcon.Warning);
    }

    /// <summary>Names a new shortcut, or way of using it, in the tooltip. Call while idle.</summary>
    public void SetShortcut(Chord shortcut, bool handsFree)
    {
        _idleTooltip = Tooltip(shortcut, handsFree);
        _recordingTooltip = RecordingTooltip(shortcut, handsFree);
        _icon.Text = _idleTooltip;
    }

    /// <summary>Shows Check for updates in the menu; only an installed copy can update.</summary>
    public void EnableUpdateChecks(Action check)
    {
        _checkForUpdates.Click += (_, _) => check();
        _checkForUpdates.Visible = true;
    }

    /// <summary>Offers an update; nothing is downloaded until the notification is clicked.</summary>
    public void ShowUpdateAvailable(string version, Action install) =>
        Notify(UpdateTitle, UpdateAvailableMessage(version), ToolTipIcon.Info, clicked: install);

    /// <summary>Answers a check from the menu that found nothing new.</summary>
    public void ShowUpToDate() => Notify(UpdateTitle, UpToDateMessage, ToolTipIcon.Info, clicked: null);

    /// <summary>Tells the user an update check or download failed.</summary>
    public void ShowUpdateFailed() => Notify(UpdateTitle, UpdateFailedMessage, ToolTipIcon.Warning, clicked: null);

    /// <summary>The update was accepted during a dictation; clicking again later installs it.</summary>
    public void ShowUpdateWaits(Action install) =>
        Notify(UpdateTitle, UpdateWaitsMessage, ToolTipIcon.Info, clicked: install);

    /// <summary>Tells the user to add an API key; clicking the notification opens Settings.</summary>
    public void ShowApiKeyNeeded() =>
        Notify(ApiKeyNeededTitle, ApiKeyNeededMessage, ToolTipIcon.Warning, clicked: _openSettings);

    /// <summary>Shows the dictation state in the icon and its tooltip.</summary>
    public void ShowState(DictationState state)
    {
        _icon.Icon = state switch
        {
            DictationState.Recording => _recordingImage,
            DictationState.Transcribing or DictationState.Inserting => _transcribingImage,
            _ => _image,
        };
        _icon.Text = StateTooltip(state, _idleTooltip, _recordingTooltip);
    }

    /// <summary>Tells the user why a recording couldn't be transcribed.</summary>
    public void ShowTranscriptionError(TranscriptionError error) =>
        Notify(TranscriptionErrorTitle, TranscriptionErrorMessage(error), ToolTipIcon.Warning, clicked: null);

    /// <summary>Tells the user a recording was silent, so nothing was sent.</summary>
    public void ShowNothingHeard() =>
        Notify(TranscriptionErrorTitle, NothingHeardMessage, ToolTipIcon.Info, clicked: null);

    /// <summary>Says that the PC is offline, so Naad is working on this PC instead of the cloud.</summary>
    public void ShowOfflineFallback() => Notify(
        "Naad is offline",
        "There's no internet connection, so Naad is transcribing and cleaning up on this PC until it's back.",
        ToolTipIcon.Info,
        clicked: null);

    /// <summary>
    /// Tells the user their text was kept, and enables Copy last transcript. Clicking the
    /// notification copies it too.
    /// </summary>
    public void ShowTranscriptKept(TranscriptKeptReason reason)
    {
        _copyLastTranscript.Enabled = true;
        _notificationClicked = () => _ = CopyAsync();
        _icon.ShowBalloonTip(
            NotificationMilliseconds, TranscriptKeptTitle, TranscriptKeptMessage(reason), ToolTipIcon.Info);
    }

    // Draws a coloured dot with a white ring in the bottom-right quarter of the icon.
    private static Icon WithDot(Icon icon, Color color)
    {
        using var bitmap = icon.ToBitmap();
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var size = bitmap.Width / 2;
            var dot = new Rectangle(bitmap.Width - size, bitmap.Height - size, size - 1, size - 1);
            using var ring = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            graphics.FillEllipse(ring, dot);
            dot.Inflate(-Math.Max(1, size / 6), -Math.Max(1, size / 6));
            graphics.FillEllipse(fill, dot);
        }

        // GetHicon makes an icon handle Naad owns; the clone copies it, so the handle is freed.
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            User32.DestroyIcon(handle);
        }
    }

    private void Notify(string title, string message, ToolTipIcon kind, Action? clicked)
    {
        _notificationClicked = clicked;
        _icon.ShowBalloonTip(NotificationMilliseconds, title, message, kind);
    }

    private static void OpenPrivacySettings() =>
        Process.Start(new ProcessStartInfo(MicrophonePrivacySettings) { UseShellExecute = true })?.Dispose();

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task CopyAsync()
    {
        try
        {
            if (!await _copy())
            {
                _notificationClicked = null;
                _icon.ShowBalloonTip(
                    NotificationMilliseconds, TranscriptKeptTitle, CopyFailedMessage, ToolTipIcon.Warning);
            }
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.TranscriptCopyFailed, e);
        }
    }

    public void Dispose()
    {
        // Hiding first removes the icon at once; otherwise a dead icon can linger in the
        // notification area until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _image.Dispose();
        _recordingImage.Dispose();
        _transcribingImage.Dispose();
    }
}
