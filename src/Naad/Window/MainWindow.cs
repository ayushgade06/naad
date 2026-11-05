using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Naad.Native;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;

namespace Naad.Window;

/// <summary>A page of the main window: its name in the navigation, and how to build it.</summary>
/// <param name="Name">Shown in the navigation list.</param>
/// <param name="Create">Builds the page each time it is shown, so it shows current data.</param>
internal sealed record MainPage(string Name, Func<UIElement> Create);

/// <summary>
/// Naad's main window (WPF): a navigation list on the left and the chosen page on the right. It
/// is built when opened and disposed of when closed, so it holds no memory while Naad sits in the
/// tray. It runs on the UI thread, inside the WinForms message loop. It never takes part in a
/// dictation: closing or using it changes nothing while one runs.
/// </summary>
internal sealed class MainWindow : System.Windows.Window
{
    // Room for the settings tabs without scrolling on a 1366×768 laptop at 100 % scale.
    private const double StartWidth = 900;
    private const double StartHeight = 700;
    private const double NavigationWidth = 180;

    private readonly IReadOnlyList<MainPage> _pages;
    private readonly ListBox _navigation = new();
    private readonly ContentControl _content = new();
    private readonly Palette _palette;

    /// <param name="pages">The pages, in navigation order.</param>
    /// <param name="palette">The colours to draw with.</param>
    /// <param name="dark">Whether the title bar should be dark.</param>
    /// <param name="bounds">Where to open, in screen pixels; null to open in the middle of the screen.</param>
    public MainWindow(IReadOnlyList<MainPage> pages, Palette palette, bool dark, Rectangle? bounds)
    {
        _pages = pages;
        _palette = palette;
        Title = "Naad";
        Width = StartWidth;
        Height = StartHeight;
        WindowStartupLocation = bounds is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        Background = Brush(palette.Background);
        Foreground = Brush(palette.Text);
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;
        Icon = AppIcon();

        _navigation.Width = NavigationWidth;
        _navigation.Background = Brush(palette.Surface);
        _navigation.Foreground = Brush(palette.Text);
        _navigation.BorderBrush = Brush(palette.Border);
        _navigation.BorderThickness = new Thickness(0, 0, 1, 0);
        foreach (var page in pages)
        {
            _navigation.Items.Add(new ListBoxItem { Content = page.Name, Padding = new Thickness(16, 10, 16, 10) });
        }

        _navigation.SelectionChanged += (_, _) => Show(_navigation.SelectedIndex);
        var layout = new DockPanel();
        DockPanel.SetDock(_navigation, Dock.Left);
        layout.Children.Add(_navigation);
        layout.Children.Add(_content);
        Content = layout;

        // Esc closes the window, as in Naad's dialogs, unless it belongs to what is being typed.
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape
                && EscapeKey.Closes(System.Windows.Input.Keyboard.FocusedElement))
            {
                Close();
            }
        };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            Dwmapi.SetDarkTitleBar(handle, dark);

            // In pixels, before the window shows: WPF's own units would shift on a scaled monitor.
            // ponytail: moving onto a monitor with another scale makes WPF resize by the ratio, so
            // the size can drift between monitors of different scaling; set it again after
            // DpiChanged if users see it (M-UI1).
            if (bounds is { } b)
            {
                _ = User32.SetWindowPos(
                    handle, 0, b.X, b.Y, b.Width, b.Height, User32.SwpNoZOrder | User32.SwpNoActivate);
            }
        };

        // A hosted settings form holds a capture and timers; it goes with the window.
        Closed += (_, _) => (_content.Content as IDisposable)?.Dispose();
    }

    /// <summary>Shows the page called <paramref name="name"/>. Call on the UI thread.</summary>
    public void ShowPage(string? name)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (name is null)
        {
            return;
        }

        for (var i = 0; i < _pages.Count; i++)
        {
            if (_pages[i].Name == name)
            {
                _navigation.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>The page shown, by name; null before one is.</summary>
    public string? CurrentPage => _navigation.SelectedIndex >= 0 ? _pages[_navigation.SelectedIndex].Name : null;

    /// <summary>
    /// Where the window is now, in screen pixels, on which monitor; null while it is minimised or
    /// maximised, so only a place the user chose is kept.
    /// </summary>
    public SavedWindow? Placement()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (WindowState != WindowState.Normal || !User32.GetWindowRect(handle, out var r))
        {
            return null;
        }

        var place = new SavedWindow(
            Screen.FromHandle(handle).DeviceName, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return place.IsValid ? place : null;
    }

    /// <summary>Naad's icon, for a WPF window's title bar.</summary>
    public static System.Windows.Media.ImageSource AppIcon()
    {
        using var icon = Tray.TrayIcon.LoadIcon();
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
    }

    /// <summary>A solid brush from an ARGB colour such as 0xFF1C1C1E.</summary>
    public static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private void Show(int index)
    {
        if (index < 0)
        {
            return;
        }

        // A page that was replaced may hold resources, such as a hosted WinForms control.
        (_content.Content as IDisposable)?.Dispose();
        var page = _pages[index].Create();
        if (page is FrameworkElement element)
        {
            element.Margin = new Thickness(24);
            TextElement.SetForeground(element, Brush(_palette.Text));
        }

        _content.Content = page;
    }
}
