using System.Windows;
using System.Windows.Controls;
using Naad.Settings;
using Naad.Snippets;
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
        var count = Muted(palette, "");
        void Save(IReadOnlyList<string> words)
        {
            note.Text = actions.SaveWords(words) ?? "";
            Fill();
        }

        void Fill()
        {
            list.Children.Clear();
            var all = actions.Words();
            count.Text = all.Count == 1 ? "1 word" : $"{all.Count} words";
            var shown = all.Where(w => w.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .ToList();
            if (shown.Count == 0)
            {
                list.Children.Add(Muted(palette, all.Count == 0
                    ? "No words yet. Add names and terms above, or select them in the History."
                    : "No matches."));
            }

            foreach (var word in shown)
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
        void Add()
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
        }

        top.Children.Add(Button("Add", Add));
        add.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Add();
                e.Handled = true;
            }
        };
        search.TextChanged += (_, _) => Fill();
        Fill();
        return Page(
            "Dictionary",
            "Names and terms Naad should spell right. They go to the speech model and the AI cleanup with " +
            "each dictation.",
            palette,
            top,
            count,
            note,
            Scroll(list));
    }

    /// <summary>
    /// The snippets: a spoken trigger and the text it expands into. Searchable; each edit is saved
    /// when its box loses focus or on Ctrl+Enter, and a trigger used twice is pointed out.
    /// </summary>
    public static UIElement Snippets(PageActions actions, Palette palette)
    {
        var list = new StackPanel();
        var note = Muted(palette, "");
        var search = new TextBox { Width = 220, Margin = new Thickness(0, 0, 16, 0) };
        var items = actions.Snippets.Load().ToList();
        void Save()
        {
            var complete = items.Where(s => s.IsComplete).ToList();
            var duplicates = TranscriptCommands.DuplicateTriggers(complete);
            note.Text = !actions.Snippets.Save(complete) ? "The snippets couldn't be saved."
                : duplicates.Count > 0 ? $"More than one snippet uses \"{duplicates[0]}\"; only the first is used."
                : complete.Count < items.Count ? "Saved. Give each snippet a trigger and a text to keep it."
                : "Saved.";
            actions.SnippetsChanged();
        }

        void Fill()
        {
            list.Children.Clear();
            var query = search.Text.Trim();
            var shown = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var snippet = items[i];
                if (query.Length > 0 && !snippet.Trigger.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    && !snippet.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                shown++;
                var trigger = new TextBox { Text = snippet.Trigger, Width = 180, Margin = new Thickness(0, 0, 8, 0) };
                var text = new TextBox
                {
                    Text = snippet.Text,
                    Width = 320,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 8, 0),
                };

                // Saved as each box is left, so nothing typed is lost to a closed window.
                void Commit()
                {
                    var edited = new Snippet(trigger.Text.Trim(), text.Text);
                    if (index < items.Count && edited != items[index])
                    {
                        items[index] = edited;
                        Save();
                    }
                }

                trigger.LostFocus += (_, _) => Commit();
                text.LostFocus += (_, _) => Commit();

                // Enter makes a new line in the text, so Ctrl+Enter is the way to save from the keyboard.
                void SaveOnCtrlEnter(object sender, System.Windows.Input.KeyEventArgs e)
                {
                    if (e.Key == System.Windows.Input.Key.Enter
                        && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
                    {
                        Commit();
                        e.Handled = true;
                    }
                }

                trigger.PreviewKeyDown += SaveOnCtrlEnter;
                text.PreviewKeyDown += SaveOnCtrlEnter;
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

            if (shown == 0)
            {
                list.Children.Add(Muted(palette, items.Count == 0 ? "No snippets yet." : "No matches."));
            }
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        buttons.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        buttons.Children.Add(search);
        buttons.Children.Add(Button("Add snippet", () =>
        {
            search.Text = "";
            items.Add(Snippet.Blank);
            Fill();
        }));
        search.TextChanged += (_, _) => Fill();
        Fill();
        return Page(
            "Snippets",
            "Say a trigger, such as \"my address\", and its text is pasted instead. Triggers match whole words, " +
            "in any case; a trigger said on its own is pasted exactly, without AI cleanup. Edits are saved as " +
            "you make them. Snippets are stored encrypted for your Windows account.",
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
            Margin = new Thickness(0, 0, 6, 0),
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
