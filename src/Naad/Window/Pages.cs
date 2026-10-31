using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using Naad.Hotkey;
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

    /// <summary>A greeting and how to dictate. Stats arrive with history.</summary>
    public static UIElement HomePage(Chord shortcut, bool handsFree, Palette palette) => Stack(
        palette,
        Heading("Naad is ready"),
        Text(handsFree
            ? $"Press {shortcut.Describe()} to start dictating into any app, and again to stop."
            : $"Hold {shortcut.Describe()} and speak; let go and your words appear where you are typing."),
        Muted("Your words, pace and time saved will gather here as you dictate."));

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
