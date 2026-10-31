using System.Windows.Forms.Integration;
using Naad.Settings;

namespace Naad.Window;

/// <summary>
/// Opens Naad's main window and setup guide, one of each at a time, built when opened and dropped
/// when closed. Use on the UI thread.
/// </summary>
/// <param name="settings">The settings in use (or waiting to apply), read each time a window opens.</param>
/// <param name="microphones">The connected recording devices.</param>
/// <param name="startsAtLogin">Whether Naad starts at sign-in now.</param>
/// <param name="actions">What the settings and the guide's buttons do.</param>
/// <param name="exit">Quits Naad, for when its tray icon is hidden.</param>
internal sealed class WindowHost(
    Func<AppSettings> settings,
    Func<IReadOnlyList<string>> microphones,
    Func<bool> startsAtLogin,
    Func<SettingsWindowActions> actions,
    Action exit)
{
    private MainWindow? _main;
    private SetupWizard? _wizard;

    /// <summary>The settings page's form while it is shown, for the shortcut capture to reach.</summary>
    public SettingsWindow? SettingsForm { get; private set; }

    /// <summary>
    /// Opens the main window at <paramref name="page"/>, or brings it forward; null keeps the page
    /// shown, so unsaved settings stay.
    /// </summary>
    public void Open(string? page)
    {
        if (_main is null)
        {
            var (palette, dark) = Colours();
            _main = new MainWindow(
                [
                    new(Pages.Home, () => Pages.HomePage(settings(), palette, exit)),
                    new(Pages.History, () => Pages.Placeholder(
                        "History",
                        "Your past dictations will be listed here, to find, copy and paste again.",
                        palette)),
                    new(Pages.Dictionary, () => Pages.Placeholder(
                        "Dictionary", "The names and terms Naad should spell right will be listed here.", palette)),
                    new(Pages.Snippets, () => Pages.Placeholder(
                        "Snippets", "Short phrases that expand into saved text will be listed here.", palette)),
                    new(Pages.Settings, () => Pages.SettingsPage(SettingsForm = SettingsPage(dark ? palette : null))),
                ],
                palette,
                dark);
            _main.Closed += (_, _) =>
            {
                _main = null;
                SettingsForm = null;
            };

            // Without this, a WPF window inside the WinForms message loop gets no keyboard input.
            ElementHost.EnableModelessKeyboardInterop(_main);
            _main.Show();
        }

        _main.ShowPage(page ?? (_main.IsLoaded ? null : Pages.Home));
        _main.Activate();
    }

    // Forgotten once disposed, so the shortcut capture never reaches a page that is gone.
    private SettingsWindow SettingsPage(Palette? palette)
    {
        var form = new SettingsWindow(
            settings(), microphones(), startsAtLogin(), actions(), palette, embedded: true);
        form.Disposed += (_, _) =>
        {
            if (SettingsForm == form)
            {
                SettingsForm = null;
            }
        };
        return form;
    }

    /// <summary>Opens the setup guide, or brings it forward.</summary>
    public void OpenSetup()
    {
        if (_wizard is null)
        {
            var (palette, dark) = Colours();
            _wizard = new SetupWizard(settings, microphones(), startsAtLogin(), actions(), palette, dark);
            _wizard.Closed += (_, _) => _wizard = null;
            ElementHost.EnableModelessKeyboardInterop(_wizard);
            _wizard.Show();
        }

        _wizard.Activate();
    }

    /// <summary>Closes both windows, as at exit.</summary>
    public void CloseAll()
    {
        _wizard?.Close();
        _main?.Close();
    }

    private (Palette Palette, bool Dark) Colours()
    {
        var choice = settings().Theme;
        var light = Theme.WindowsUsesLightApps();
        return (Theme.For(choice, light), Theme.IsDark(choice, light));
    }
}
