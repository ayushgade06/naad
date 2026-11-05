using System.Diagnostics;
using System.Globalization;
using Naad.Audio;
using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>
/// The General tab's system rows: automatic update checks with Check now and the last check, the
/// clipboard choices, and whether Windows lets Naad use the microphone. Use on the UI thread.
/// </summary>
internal sealed partial class GeneralPanel
{
    // Windows' microphone privacy page.
    private const string MicrophonePrivacySettings = "ms-settings:privacy-microphone";

    private readonly CheckBox _automaticUpdates = new() { Text = "Check for updates automatically", AutoSize = true };
    private readonly CheckBox _restoreClipboard = new()
    {
        Text = "Put back what was on the clipboard after a dictation is pasted",
        AutoSize = true,
    };

    private readonly CheckBox _outOfHistory = new()
    {
        Text = "Keep dictations out of clipboard history (Win+V) and cloud clipboard",
        AutoSize = true,
    };

    private readonly Label _lastChecked = Note();
    private readonly Label _microphoneAccess = Note();
    private bool _checking;

    private void AddSystemRows(AppSettings current, Func<Task?>? checkForUpdates)
    {
        _automaticUpdates.Checked = current.AutomaticUpdateChecks;
        _restoreClipboard.Checked = current.RestoreClipboard;
        _outOfHistory.Checked = current.KeepOutOfClipboardHistory;
        var checkNow = new Button { Text = "Check now", AutoSize = true };
        checkNow.Click += async (_, _) =>
        {
            if (checkForUpdates?.Invoke() is not { } check)
            {
                _lastChecked.Text = "Only a copy installed with Naad's installer updates itself.";
                return;
            }

            checkNow.Enabled = false;
            _checking = true;
            _lastChecked.Text = "Checking... the answer shows in a notification.";
            try
            {
                await check;
            }
            catch (Exception e)
            {
                // From a click: anything not caught here would end the app.
                Log.Error(LogEvent.UpdateCheckFailed, e);
            }
            finally
            {
                _checking = false;
                checkNow.Enabled = true;
                ShowSystemStatus();
            }
        };
        var openSettings = new Button { Text = "Open Windows settings", AutoSize = true };
        openSettings.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(MicrophonePrivacySettings) { UseShellExecute = true })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // Windows without the Settings app: nothing to open.
                Log.Error(LogEvent.SettingsPageOpenFailed, e);
            }
        };

        Row(this, "Updates", _automaticUpdates);
        Row(this, "", Flow(checkNow, _lastChecked));
        Row(this, "Clipboard", _restoreClipboard);
        Row(this, "", _outOfHistory);
        Row(this, "Microphone", Flow(_microphoneAccess, openSettings));
        ShowSystemStatus();

        // Back from Windows' settings, or after a check: the status is read again.
        HandleCreated += (_, _) =>
        {
            if (FindForm() is { } form)
            {
                form.Activated += (_, _) => ShowSystemStatus();
            }
        };
        VisibleChanged += (_, _) => ShowSystemStatus();
    }

    private AppSettings ApplySystem(AppSettings chosen) => chosen with
    {
        AutomaticUpdateChecks = _automaticUpdates.Checked,
        RestoreClipboard = _restoreClipboard.Checked,
        KeepOutOfClipboardHistory = _outOfHistory.Checked,
    };

    // The registry and a small file only: quick enough for each time the window comes forward.
    private void ShowSystemStatus()
    {
        _microphoneAccess.Text = MicrophoneFailure.Describe(AudioRecorder.Access());
        if (_checking)
        {
            return;
        }

        var last = Updates.UpdateState.Load(Updates.UpdateState.DefaultPath).LastChecked;
        _lastChecked.Text = last is { } when
            ? $"Last checked {when.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}"
            : "Not checked yet";
    }
}
