using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Naad.History;
using Naad.Settings;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Naad.Window;

/// <summary>Builds the main window's pages. Each is rebuilt when shown, so it shows current data.</summary>
internal static class Pages
{
    public const string Home = "Home";
    public const string History = "History";
    public const string Dictionary = "Dictionary";
    public const string Snippets = "Snippets";
    public const string Settings = "Settings";

    /// <summary>
    /// A greeting, how to dictate, the stats from the history (words, pace, time saved, streak),
    /// and Quit for when the tray icon is hidden.
    /// </summary>
    public static UIElement HomePage(
        AppSettings s, IReadOnlyList<HistoryRecord> records, Palette palette, Action exit, Action setup)
    {
        var stats = DictationStats.Compute(
            records, DateOnly.FromDateTime(DateTime.Now), TimeZoneInfo.Local);
        var how = s.HandsFree
            ? $"Press {s.DictationShortcut.Describe()} to start dictating into any app, and again to stop."
            : $"Hold {s.DictationShortcut.Describe()} and speak; let go and your words appear where you are typing.";
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
        row.Children.Add(Stat("Words dictated", stats.Words.ToString("N0", CultureInfo.CurrentCulture), palette));
        row.Children.Add(Stat("Words per minute", stats.WordsPerMinute > 0 ? stats.WordsPerMinute.ToString(
            CultureInfo.CurrentCulture) : "—", palette));
        row.Children.Add(Stat("Time saved", $"{(int)stats.TimeSaved.TotalMinutes} min", palette));
        row.Children.Add(Stat("Day streak", stats.DayStreak.ToString(CultureInfo.CurrentCulture), palette));
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("Run setup again", setup));
        buttons.Children.Add(Button("Quit Naad", exit));
        return Stack(
            palette,
            Heading(records.Count == 0 ? "Welcome to Naad" : "Welcome back"),
            Text(how),
            records.Count == 0 ? Muted("Your words will gather here as you dictate.") : row,
            Muted("Time saved compares your speaking time with typing at 40 words a minute."),
            buttons);
    }

    private static Border Stat(string label, string value, Palette palette)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = value, FontSize = 24, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = label, Foreground = MainWindow.Brush(palette.Muted) });
        return new Border
        {
            Child = stack,
            Padding = new Thickness(16, 10, 16, 10),
            Margin = new Thickness(0, 0, 12, 12),
            MinWidth = 140,
            Background = MainWindow.Brush(palette.Surface),
            BorderBrush = MainWindow.Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private static System.Windows.Controls.Button Button(string label, Action click)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = label,
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
        };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A page whose content arrives in a later version.</summary>
    public static UIElement Placeholder(string title, string text, Palette palette) =>
        Stack(palette, Heading(title), Muted(text));

    /// <summary>
    /// The settings window, embedded: its fields, checks and saving are the same as the
    /// stand-alone window's. Disposing the page disposes the hosted form.
    /// </summary>
    public static UIElement SettingsPage(SettingsWindow form)
    {
        var host = new WindowsFormsHost { Child = form };
        form.Show();
        return host;
    }

    private static StackPanel Stack(Palette palette, params UIElement[] children)
    {
        var stack = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var child in children)
        {
            if (child is TextBlock { Tag: "muted" } muted)
            {
                muted.Foreground = MainWindow.Brush(palette.Muted);
            }

            stack.Children.Add(child);
        }

        return stack;
    }

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 26, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };

    private static TextBlock Text(string text) =>
        new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Tag = "muted",
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };
}
