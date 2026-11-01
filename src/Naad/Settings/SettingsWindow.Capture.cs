using Naad.Hotkey;

namespace Naad.Settings;

/// <summary>The settings window's shortcut capture: the hook records the next chord pressed.</summary>
internal sealed partial class SettingsWindow
{
    /// <summary>The hook finished a capture this window started. Call on the UI thread.</summary>
    public void ShowCapture(CaptureStep step)
    {
        if (_capturing is not { } field)
        {
            return;
        }

        _capturing = null;
        field.EndCapture(step.Chord);
        _error.Text = step.Status == CaptureStatus.Rejected ? "That key can't be part of a shortcut. Try another." : "";
    }

    private void BeginCapture(ShortcutField field)
    {
        CancelCapture();
        if (!_actions.BeginCapture())
        {
            field.EndCapture(null);
            return;
        }

        _capturing = field;
        field.ShowCapturing();
    }

    private void CancelCapture()
    {
        if (_capturing is { } field)
        {
            _actions.CancelCapture();
            _capturing = null;
            field.EndCapture(null);
        }
    }
}
