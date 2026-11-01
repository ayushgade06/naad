using System.Globalization;
using System.IO.Compression;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Naad.History;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The History page: past dictations by day, searchable, each with Copy, Paste, Play, Retry,
/// Details (raw next to cleaned text, and timings), Export, Add to dictionary and Delete. What it
/// shows is the user's own text, decrypted only to show it. Use on the UI thread.
/// </summary>
internal sealed class HistoryPage : DockPanel, IDisposable
{
    // Enough to scan a day's dictations at a glance; search finds the rest.
    private const int MaxShown = 200;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly PageActions _actions;
    private readonly Palette _palette;
    private readonly StackPanel _list = new();
    private readonly TextBox _search = new() { Width = 280, Margin = new Thickness(0, 0, 8, 0) };
    private SoundPlayer? _player;

    public HistoryPage(PageActions actions, Palette palette)
    {
        _actions = actions;
        _palette = palette;
        var clear = new Button { Content = "Delete all", Padding = new Thickness(10, 2, 10, 2) };
        clear.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(
                "Delete every dictation in the history? This can't be undone.", "Naad",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
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
        _player?.Dispose();
    }

    private void Fill()
    {
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
        var failed = record.Outcome == HistoryOutcome.Failed;
        var text = new TextBox
        {
            Text = failed ? "No transcript. Retry to transcribe the recording again." : record.Text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = MainWindow.Brush(failed ? _palette.Muted : _palette.Text),
        };
        var meta = Muted($"{local:t} · {record.App ?? "unknown app"} · {record.AudioMilliseconds / 1000.0:0.0} s"
            + (record.Selection is null ? ""
                : record.Invocation is { } invocation ? $" · edit ({invocation})"
                : " · edit")
            + (record.LiveFellBack ? " · live failed, transcribed after" : "")
            + (record.Outcome == HistoryOutcome.Kept ? " · not pasted" : ""));
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (!failed)
        {
            buttons.Children.Add(Action("Copy", async () => await _actions.Copy(record.Text)));
            buttons.Children.Add(Action("Paste", async () => await _actions.Paste(record.Text)));
        }

        if (record.HasAudio)
        {
            buttons.Children.Add(Action("Play", () => Play(record.Id)));
            buttons.Children.Add(Action("Retry", async () =>
            {
                text.Text = "Transcribing again...";
                text.Text = await _actions.Retry(record) ?? "Retry failed; the recording is kept.";
            }));
        }

        var details = Muted(
            $"Heard: {record.Raw}\nPasted: {record.Text}"
            + (record.Selection is { } selection ? $"\nSelected: {selection}" : "")
            + $"\nTranscription {record.TranscribeMilliseconds} ms · cleanup {record.CleanupMilliseconds} ms");
        details.Visibility = Visibility.Collapsed;
        buttons.Children.Add(Action("Details", () => details.Visibility = details.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible));
        if (!failed)
        {
            buttons.Children.Add(Action("Add to dictionary", () => AddToDictionary(text.SelectedText)));
        }

        buttons.Children.Add(Action("Export", () => Export(record)));
        buttons.Children.Add(Action("Delete", () => _actions.History.Delete(record.Id)));
        var stack = new StackPanel();
        stack.Children.Add(meta);
        stack.Children.Add(text);
        stack.Children.Add(details);
        stack.Children.Add(buttons);
        return new Border
        {
            Child = stack,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Background = MainWindow.Brush(_palette.Surface),
            BorderBrush = MainWindow.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private void Play(Guid id)
    {
        _player?.Dispose();
        _player = null;
        if (_actions.History.Store.Audio(id) is { } wav)
        {
            _player = new SoundPlayer(new MemoryStream(wav));
            _player.Play();
        }
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

    // On request only: the record and its recording, unencrypted, in a ZIP the user chooses.
    private static void Export(HistoryRecord record, HistoryStore store)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"naad-dictation-{record.Time.ToLocalTime():yyyy-MM-dd-HHmmss}.zip",
            Filter = "ZIP archive|*.zip",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var zip = new ZipArchive(File.Create(dialog.FileName), ZipArchiveMode.Create);
            using (var entry = zip.CreateEntry("dictation.json").Open())
            {
                JsonSerializer.Serialize(entry, record, Indented);
            }

            if (store.Audio(record.Id) is { } wav)
            {
                using var audio = zip.CreateEntry("recording.wav").Open();
                audio.Write(wav);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.HistoryExportFailed, e);
            System.Windows.MessageBox.Show("The dictation couldn't be exported there.", "Naad");
        }
    }

    private void Export(HistoryRecord record) => Export(record, _actions.History.Store);

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
            // An action's failure is shown, never a crash: async actions would otherwise end the app.
            try
            {
                await click();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
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
