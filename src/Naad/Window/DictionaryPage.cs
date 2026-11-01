using System.Windows;
using System.Windows.Controls;
using Naad.Settings;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The Dictionary and Snippets pages: lists edited in place, saved at once. Use on the UI thread.
/// </summary>
internal static class DictionaryPage
{
    /// <summary>The dictionary: search, add, rename in place, delete, with duplicates refused.</summary>
    public static UIElement Words(PageActions actions, Palette palette)
    {
        var list = new StackPanel();
        var search = new TextBox { Width = 220, Margin = new Thickness(0, 0, 16, 0) };
        var add = new TextBox { Width = 220, Margin = new Thickness(0, 0, 8, 0) };
        var note = Muted(palette, "");
        void Save(IReadOnlyList<string> words)
        {
            note.Text = actions.SaveWords(words) ?? "";
            Fill();
        }

        void Fill()
        {
            list.Children.Clear();
            foreach (var word in actions.Words().Where(w => w.Contains(search.Text.Trim(),
                StringComparison.CurrentCultureIgnoreCase)))
            {
                var box = new TextBox { Text = word, Width = 260, Margin = new Thickness(0, 0, 8, 0) };
                box.LostFocus += (_, _) =>
                {
                    var renamed = box.Text.Trim();
                    if (renamed == word)
                    {
                        return;
                    }

                    if (!AppSettings.IsValidWord(renamed)
                        || actions.Words().Any(w => w != word && w.Equals(renamed, StringComparison.OrdinalIgnoreCase)))
                    {
                        note.Text = "That entry is empty, too long, has a comma, or is already in the dictionary.";
                        box.Text = word;
                        return;
                    }

                    Save([.. actions.Words().Select(w => w == word ? renamed : w)]);
                };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(box);
                row.Children.Add(Button("Delete", () => Save([.. actions.Words().Where(w => w != word)])));
                list.Children.Add(row);
            }
        }

        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(search);
        top.Children.Add(add);
        top.Children.Add(Button("Add", () =>
        {
            var words = AppSettings.SplitWords(add.Text);
            var fresh = words.Where(w => !actions.Words().Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
            note.Text = words.Count == 0 ? "Type a word or name; separate several with commas." : "";
            if (fresh.Count > 0)
            {
                add.Text = "";
                Save([.. actions.Words(), .. fresh]);
            }
            else if (words.Count > 0)
            {
                note.Text = "Already in the dictionary.";
            }
        }));
        search.TextChanged += (_, _) => Fill();
        Fill();
        return Page(
            "Dictionary",
            "Names and terms Naad should spell right. They go to the speech model and the AI cleanup with " +
            "each dictation.",
            palette,
            top,
            note,
            Scroll(list));
    }

    /// <summary>The snippets: a spoken trigger and the text it expands into.</summary>
    public static UIElement Snippets(PageActions actions, Palette palette)
    {
        var list = new StackPanel();
        var note = Muted(palette, "");
        var items = actions.Snippets.Load().ToList();
        void Save()
        {
            var valid = items.Where(s => s.Trigger.Trim().Length > 0 && s.Text.Length > 0).ToList();
            note.Text = actions.Snippets.Save(valid) ? "Saved." : "The snippets couldn't be saved.";
            actions.SnippetsChanged();
        }

        void Fill()
        {
            list.Children.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var trigger = new TextBox { Text = items[i].Trigger, Width = 180, Margin = new Thickness(0, 0, 8, 0) };
                var text = new TextBox
                {
                    Text = items[i].Text,
                    Width = 320,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                trigger.LostFocus += (_, _) => items[index] = items[index] with { Trigger = trigger.Text.Trim() };
                text.LostFocus += (_, _) => items[index] = items[index] with { Text = text.Text };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                row.Children.Add(trigger);
                row.Children.Add(text);
                row.Children.Add(Button("Delete", () =>
                {
                    items.RemoveAt(index);
                    Save();
                    Fill();
                }));
                list.Children.Add(row);
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(Button("Add snippet", () =>
        {
            items.Add(new("trigger phrase", "Text to paste"));
            Fill();
        }));
        buttons.Children.Add(Button("Save", Save));
        Fill();
        return Page(
            "Snippets",
            "Say a trigger, such as \"my address\", and its text is pasted instead. Triggers match whole words, " +
            "in any case. Snippets are stored encrypted for your Windows account.",
            palette,
            buttons,
            note,
            Scroll(list));
    }

    private static DockPanel Page(string title, string text, Palette palette, params UIElement[] parts)
    {
        var panel = new DockPanel();
        var head = new StackPanel();
        head.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        head.Children.Add(Muted(palette, text));
        foreach (var part in parts[..^1])
        {
            head.Children.Add(part);
        }

        DockPanel.SetDock(head, Dock.Top);
        panel.Children.Add(head);
        panel.Children.Add(parts[^1]);
        return panel;
    }

    private static ScrollViewer Scroll(UIElement content) =>
        new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    private static Button Button(string label, Action click)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0,
            6, 0)
        };
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Muted(Palette palette, string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = MainWindow.Brush(palette.Muted),
    };
}
