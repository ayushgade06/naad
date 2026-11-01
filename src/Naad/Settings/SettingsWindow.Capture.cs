using Naad.Hotkey;

namespace Naad.Settings;

/// <summary>The settings window's shortcut capture: the hook records the next chord pressed.</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The hook finished a capture this window started. Call on the UI thread.</summary>
    public void ShowCapture(CaptureStep step)
    {
        if (!_capturing)
        {
            return;
        }

        if (step.Chord is { } chord)
        {
            _shortcut = chord;
        }

        EndCapture();
        _error.Text = step.Status == CaptureStatus.Rejected ? "That key can't be part of a shortcut. Try another." : "";
    }

    private void BeginCapture()
    {
        if (_capturing || !_actions.BeginCapture())
        {
            return;
        }

        _capturing = true;
        _changeShortcut.Enabled = false;
        _shortcutText.Text = CapturePrompt;
    }

    private void CancelCapture()
    {
        if (_capturing)
        {
            _actions.CancelCapture();
            EndCapture();
        }
    }

    private void EndCapture()
    {
        _capturing = false;
        _changeShortcut.Enabled = true;
        _shortcutText.Text = _shortcut.Describe();
    }
}
