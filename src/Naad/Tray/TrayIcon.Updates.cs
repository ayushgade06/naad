namespace Naad.Tray;

/// <summary>
/// The tray's update items, shown while an update is offered: "Update to version X..." and
/// "Skip version X", so the offer stays at hand after its notification is gone.
/// </summary>
internal sealed partial class TrayIcon
{
    private ToolStripMenuItem? _installUpdate;
    private ToolStripMenuItem? _skipUpdate;
    private Action? _onInstall;
    private Action? _onSkip;

    /// <summary>
    /// Shows the offer for <paramref name="version"/> under Check for updates, calling
    /// <paramref name="install"/> or <paramref name="skip"/> when chosen; a null
    /// <paramref name="version"/> hides it.
    /// </summary>
    public void ShowUpdateOffer(string? version, Action? install, Action? skip)
    {
        if (_skipUpdate is null || _installUpdate is null)
        {
            _installUpdate = new ToolStripMenuItem();
            _installUpdate.Click += (_, _) => _onInstall?.Invoke();
            _skipUpdate = new ToolStripMenuItem();
            _skipUpdate.Click += (_, _) => _onSkip?.Invoke();
            var at = _menu.Items.IndexOf(_checkForUpdates) + 1;
            _menu.Items.Insert(at, _skipUpdate);
            _menu.Items.Insert(at, _installUpdate);
        }

        (_onInstall, _onSkip) = (install, skip);
        _installUpdate.Text = $"Update to version {version}...";
        _skipUpdate.Text = $"Skip version {version}";
        _installUpdate.Visible = _skipUpdate.Visible = version is not null;
    }
}
