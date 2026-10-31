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
    public MainWindow(IReadOnlyList<MainPage> pages, Palette palette, bool dark)
    {
        _pages = pages;
        _palette = palette;
        Title = "Naad";
        Width = StartWidth;
        Height = StartHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
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

        // Esc closes the window, as in Naad's dialogs.
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };
        SourceInitialized += (_, _) => Dwmapi.SetDarkTitleBar(new WindowInteropHelper(this).Handle, dark);
    }

    /// <summary>Shows the page called <paramref name="name"/>. Call on the UI thread.</summary>
    public void ShowPage(string name)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            if (_pages[i].Name == name)
            {
                _navigation.SelectedIndex = i;
                return;
            }
        }
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
