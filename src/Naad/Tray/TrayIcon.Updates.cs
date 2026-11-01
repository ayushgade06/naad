namespace Naad.Tray;

/// <summary>The tray's Skip this version item, shown while an update is offered.</summary>
internal sealed partial class TrayIcon
{
    private ToolStripMenuItem? _skipUpdate;
    private Action? _onSkip;

    /// <summary>
    /// Shows "Skip version X" under Check for updates, calling <paramref name="skip"/> when chosen;
    /// a null <paramref name="version"/> hides it.
    /// </summary>
    public void ShowSkipUpdate(string? version, Action? skip)
    {
        if (_skipUpdate is null)
        {
            _skipUpdate = new ToolStripMenuItem();
            _skipUpdate.Click += (_, _) => _onSkip?.Invoke();
            _menu.Items.Insert(_menu.Items.IndexOf(_checkForUpdates) + 1, _skipUpdate);
        }

        _onSkip = skip;
        _skipUpdate.Text = $"Skip version {version}";
        _skipUpdate.Visible = version is not null;
    }
}
