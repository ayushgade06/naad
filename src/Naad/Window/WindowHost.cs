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
/// <param name="pages">What the History, Dictionary and Snippets pages do.</param>
internal sealed class WindowHost(
    Func<AppSettings> settings,
    Func<IReadOnlyList<string>> microphones,
    Func<bool> startsAtLogin,
    Func<SettingsWindowActions> actions,
    Action exit,
    PageActions pages)
{
    // Asked of Windows once, off the UI thread: on a work PC it can ask a domain controller and take
    // seconds. Until it answers, Home says "Welcome back". Shown only, never logged.
    private static readonly Task<string?> FirstName = Task.Run(() => Greeting.FirstName(Native.Secur32.DisplayName()));

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
                    new(Pages.Home, () => Pages.HomePage(
                        settings(),
                        pages.History.Stats(DateOnly.FromDateTime(DateTime.Now)),
                        FirstName.IsCompletedSuccessfully ? FirstName.Result : null,
                        palette,
                        exit,
                        OpenSetup)),
                    new(Pages.History, () => new HistoryPage(PasteFromWindow(), palette)),
                    new(Pages.Dictionary, () => DictionaryPage.Words(pages, palette)),
                    new(Pages.Snippets, () => DictionaryPage.Snippets(pages, palette)),
                    new(Pages.Meetings, () => new MeetingsPage(pages.Meetings, palette)),
                    new(Pages.Diagnostics, () => Pages.DiagnosticsPage(
                        Diagnostics.Describe(settings()), pages.Copy, palette)),
                    new(Pages.Settings, () => Pages.SettingsPage(SettingsForm = SettingsPage(dark ? palette : null))),
                ],
                palette,
                dark,
                WindowPlacement.Fit(settings().WindowPlacements, Screens(), MouseMonitor()));
            var main = _main;
            main.Closing += (_, _) => RememberPlace(main);
            main.Closed += (_, _) =>
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

    // Naad's window has focus when Paste is clicked: it steps aside first, so the text goes into
    // the window that was in front of it.
    private PageActions PasteFromWindow() => pages with
    {
        Paste = async text =>
        {
            if (_main is { } main)
            {
                main.WindowState = System.Windows.WindowState.Minimized;
            }

            // Long enough for Windows to hand focus back to the window before Naad's.
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            return await pages.Paste(text);
        },
    };

    // Forgotten once disposed, so the shortcut capture never reaches a page that is gone. A new
    // theme reopens the window on the same page, drawn in the new colours.
    private SettingsWindow SettingsPage(Palette? palette)
    {
        var inner = actions();
        var withTheme = inner with
        {
            Save = change =>
            {
                var theme = settings().Theme;
                var error = inner.Save(change);
                if (error is null && change.Settings.Theme != theme && _main is { } main)
                {
                    // After the click that saved: the form doing the saving goes with the window.
                    _ = main.Dispatcher.BeginInvoke(() => Reopen(main));
                }

                return error;
            },
        };
        var form = new SettingsWindow(
            settings(), microphones(), startsAtLogin(), withTheme, palette, embedded: true);
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
            _wizard = new SetupWizard(
                settings, microphones(), startsAtLogin(), actions(), pages.Dictation, palette, dark);
            _wizard.Closed += (_, _) => _wizard = null;
            ElementHost.EnableModelessKeyboardInterop(_wizard);
            _wizard.Show();
        }

        _wizard.Activate();
    }

    private void Reopen(MainWindow main)
    {
        var page = main.CurrentPage;
        main.Close();
        Open(page ?? Pages.Settings);
    }

    // Saved through the settings, like any other choice; a failure only loses the place.
    private void RememberPlace(MainWindow main)
    {
        if (main.Placement() is not { } place)
        {
            return;
        }

        var s = settings();
        var places = WindowPlacement.Remember(s.WindowPlacements, place);
        if (places.SequenceEqual(s.WindowPlacements))
        {
            return;
        }

        _ = actions().Save(new(
            s with { WindowPlacements = places },
            new Dictionary<Providers.CloudService, string>(),
            startsAtLogin()));
    }

    private static List<Tray.OverlayScreen> Screens() =>
        [.. Screen.AllScreens.Select(s => new Tray.OverlayScreen(s.DeviceName, s.WorkingArea, s.Primary))];

    private static string MouseMonitor() => Screen.FromPoint(Cursor.Position).DeviceName;

    /// <summary>The hook finished a capture: it goes to whichever window started one.</summary>
    public void ShowCapture(Hotkey.CaptureStep step)
    {
        SettingsForm?.ShowCapture(step);
        _wizard?.ShowCapture(step);
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
