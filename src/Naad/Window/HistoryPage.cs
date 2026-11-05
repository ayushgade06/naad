using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Naad.History;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The History page: past dictations by day, searchable, each with Copy, Paste, Play, Retry,
/// Details (context, transcription and cleanup), Export, Add to dictionary and Delete. What it
/// shows is the user's own text, decrypted only to show it. Use on the UI thread.
/// </summary>
internal sealed class HistoryPage : DockPanel, IDisposable
{
    // Enough to scan a day's dictations at a glance; search finds the rest.
    private const int MaxShown = 200;

    // Long enough to read "Copied", short enough that the button is soon ready again.
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(1.5);

    // Ten steps a second moves the bar smoothly without noticeable work.
    private static readonly TimeSpan ProgressTick = TimeSpan.FromMilliseconds(100);

    // About a dozen lines: the prompt's start is readable, and it scrolls for the rest.
    private const double PromptHeight = 240;

    private readonly PageActions _actions;
    private readonly Palette _palette;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Width = 280, Margin = new Thickness(0, 0, 8, 0) };
    private readonly HashSet<Guid> _retrying = [];
    private SoundPlayer? _player;
    private DispatcherTimer? _progress;
    private Action? _resetPlaying;
    private Guid? _playing;
    private int _playRequests;

    public HistoryPage(PageActions actions, Palette palette)
    {
        _actions = actions;
        _palette = palette;
        var clear = new Button { Content = "Delete all", Padding = new Thickness(10, 2, 10, 2) };
        clear.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(
                "Delete every dictation in the history? This can't be undone. The stats on the Home page " +
                "stay.", "Naad", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _actions.History.DeleteAll();
            }
        };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(_search);
        top.Children.Add(clear);
        SetDock(top, Dock.Top);
        Children.Add(top);
        Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _search.TextChanged += (_, _) => Fill();
        _actions.History.Changed += Fill;
        Fill();
    }

    public void Dispose()
    {
        _actions.History.Changed -= Fill;
        StopPlaying();
    }

    private void Fill()
    {
        // The rows are rebuilt, so the one playing would lose its Stop button.
        StopPlaying();
        _list.Children.Clear();
        var query = _search.Text.Trim();
        var records = _actions.History.Records
            .Where(r => query.Length == 0
                || r.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || r.Raw.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || (r.App?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .Take(MaxShown)
            .ToList();
        if (records.Count == 0)
        {
            _list.Children.Add(Muted(query.Length > 0
                ? "No dictation matches."
                : "Your dictations will gather here. The history setting on the General tab says how long " +
                    "they are kept."));
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        DateOnly? day = null;
        foreach (var record in records)
        {
            var local = record.Time.ToLocalTime();
            var date = DateOnly.FromDateTime(local.DateTime);
            if (date != day)
            {
                day = date;
                _list.Children.Add(new TextBlock
                {
                    Text = DictationStats.DayLabel(date, today, CultureInfo.CurrentCulture),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 16, 0, 6),
                });
            }

            _list.Children.Add(Row(record, local));
        }
    }

    private Border Row(HistoryRecord record, DateTimeOffset local)
    {
        var placeholder = HistoryDetails.Placeholder(record);
        var text = new TextBox
        {
            Text = placeholder ?? record.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = MainWindow.Brush(placeholder is null ? _palette.Text : _palette.Muted),
        };
        var meta = Muted($"{local:t} · {record.App ?? "unknown app"} · {record.AudioMilliseconds / 1000.0:0.0} s"
            + (record.Selection is null ? ""
                : record.Invocation is { } invocation ? $" · edit ({invocation})"
                : " · edit")
            + (record.LiveFellBack ? " · live failed, transcribed after" : "")
            + (record.Outcome == HistoryOutcome.Kept ? " · not pasted" : ""));
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var progress = new ProgressBar
        {
            Height = 4,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        if (HistoryDetails.HasText(record))
        {
            buttons.Children.Add(CopyButton("Copy", record.Text));
            buttons.Children.Add(Action("Paste", async () => await _actions.Paste(record.Text)));
        }

        if (record.HasAudio)
        {
            Button play = null!;
            play = Action("Play", () => TogglePlayAsync(record, play, progress));
            buttons.Children.Add(play);
            Button retry = null!;
            retry = Action("Retry", () => RetryAsync(record, retry, text));
            retry.IsEnabled = !_retrying.Contains(record.Id);
            buttons.Children.Add(retry);
        }

        var details = Details(record);
        details.Visibility = Visibility.Collapsed;
        buttons.Children.Add(Action("Details", () => details.Visibility = details.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible));
        if (HistoryDetails.HasText(record))
        {
            buttons.Children.Add(Action("Add to dictionary", () => AddToDictionary(text.SelectedText)));
        }

        buttons.Children.Add(Action("Export", () => HistoryExport.ExportAsync(record, _actions.History.Store)));
        buttons.Children.Add(Action("Delete", () => _actions.History.Delete(record.Id)));
        var stack = new StackPanel();
        stack.Children.Add(meta);
        stack.Children.Add(text);
        stack.Children.Add(progress);
        stack.Children.Add(details);
        stack.Children.Add(buttons);
        return Card(stack, new Thickness(12), new Thickness(0, 0, 0, 8));
    }

    // Three blocks: what the context model saw, what was heard, and what cleanup made of it.
    private StackPanel Details(HistoryRecord record)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(Block("Context", Muted(record.Summary ?? "None")));

        var heard = new StackPanel();
        heard.Children.Add(Muted(record.Raw.Length > 0 ? record.Raw : "Nothing was heard."));
        if (record.Selection is { } selection)
        {
            heard.Children.Add(Muted($"Selected: {selection}"));
        }

        heard.Children.Add(Muted($"{record.TranscribeMilliseconds} ms"));
        if (record.Raw.Length > 0)
        {
            heard.Children.Add(CopyButton("Copy raw", record.Raw));
        }

        panel.Children.Add(Block("Transcription", heard));

        var cleaned = new StackPanel();
        cleaned.Children.Add(Muted($"{HistoryDetails.Cleanup(record.Cleanup)} {record.CleanupMilliseconds} ms"));
        if (record.Prompt is { } sent)
        {
            var prompt = new TextBox
            {
                Text = sent,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = PromptHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 4),
            };
            Button toggle = null!;
            toggle = Action("Show prompt", () =>
            {
                var show = prompt.Visibility != Visibility.Visible;
                prompt.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = show ? "Hide prompt" : "Show prompt";
            });
            cleaned.Children.Add(toggle);
            cleaned.Children.Add(prompt);
        }

        if (HistoryDetails.HasText(record))
        {
            cleaned.Children.Add(Muted(record.Text));
            cleaned.Children.Add(CopyButton("Copy cleaned", record.Text));
        }

        panel.Children.Add(Block("Cleanup", cleaned));
        return panel;
    }

    private Border Block(string title, UIElement body)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(body);
        return Card(stack, new Thickness(8), new Thickness(0, 0, 0, 6));
    }

    private Border Card(UIElement child, Thickness padding, Thickness margin) => new()
    {
        Child = child,
        Padding = padding,
        Margin = margin,
        Background = MainWindow.Brush(_palette.Surface),
        BorderBrush = MainWindow.Brush(_palette.Border),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
    };

    // Says "Copied" for a moment, so the click is seen to work; another click starts the moment again.
    private Button CopyButton(string label, string text)
    {
        var timer = new DispatcherTimer { Interval = CopiedFor };
        Button button = null!;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = label;
        };
        button = Action(label, async () =>
        {
            button.Content = await _actions.Copy(text) ? "Copied" : "Clipboard busy";
            timer.Stop();
            timer.Start();
        });
        return button;
    }

    private async Task RetryAsync(HistoryRecord record, Button button, TextBox text)
    {
        // One retry per record at a time: a second click would transcribe and save it twice.
        if (!_retrying.Add(record.Id))
        {
            return;
        }

        button.IsEnabled = false;
        text.Text = "Transcribing again...";
        try
        {
            text.Text = await _actions.Retry(record) ?? "Nothing to paste came of it; the recording is kept.";
        }
        finally
        {
            _retrying.Remove(record.Id);
            button.IsEnabled = true;
        }

        // The update rebuilt the list while the id was still guarded, with Retry greyed out.
        Fill();
    }

    // Play and Stop share the button. SoundPlayer has no "finished" event, so the bar runs on the
    // recording's known length.
    private async Task TogglePlayAsync(HistoryRecord record, Button button, ProgressBar bar)
    {
        var request = ++_playRequests;
        var wasPlaying = _playing == record.Id;
        StopPlaying();
        if (wasPlaying)
        {
            return;
        }

        var wav = await Task.Run(() => _actions.History.Store.Audio(record.Id));
        if (wav is null || request != _playRequests)
        {
            return;
        }

        _player = new SoundPlayer(new MemoryStream(wav));
        _player.Play();
        _playing = record.Id;
        button.Content = "Stop";
        bar.Maximum = Math.Max(1, record.AudioMilliseconds);
        bar.Value = 0;
        bar.Visibility = Visibility.Visible;
        var started = Stopwatch.GetTimestamp();
        _progress = new DispatcherTimer { Interval = ProgressTick };
        _progress.Tick += (_, _) =>
        {
            bar.Value = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            // A recording of unknown length plays to its end; only the bar can't show where it is.
            if (record.AudioMilliseconds > 0 && bar.Value >= bar.Maximum)
            {
                StopPlaying();
            }
        };
        _resetPlaying = () =>
        {
            button.Content = "Play";
            bar.Visibility = Visibility.Collapsed;
        };
        _progress.Start();
    }

    private void StopPlaying()
    {
        _progress?.Stop();
        _progress = null;

        // Disposing alone leaves the sound playing to its end.
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _playing = null;
        _resetPlaying?.Invoke();
        _resetPlaying = null;
    }

    // The words selected in a record's text, or a prompt to select them first.
    private void AddToDictionary(string selected)
    {
        var words = Settings.AppSettings.SplitWords(selected);
        if (words.Count == 0)
        {
            System.Windows.MessageBox.Show("Select a word or name in the text first.", "Naad");
            return;
        }

        var merged = _actions.Words().Concat(words).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var added = $"Added to the dictionary: {string.Join(", ", words)}.";
        System.Windows.MessageBox.Show(_actions.SaveWords(merged) ?? added, "Naad");
    }

    private static Button Action(string label, Action click) => Action(label, () =>
    {
        click();
        return Task.CompletedTask;
    });

    private static Button Action(string label, Func<Task> click)
    {
        var button = new Button
        {
            Content = label,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(0, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += async (_, _) =>
        {
            // From a click: an exception escaping an async handler would end the app.
            try
            {
                await click();
            }
            catch (Exception e)
            {
                Log.Error(LogEvent.HistoryActionFailed, e);
            }
        };
        return button;
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = MainWindow.Brush(_palette.Muted),
    };
}
