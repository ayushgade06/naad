using System.Windows;
using System.Windows.Controls;
using Naad.Meetings;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// One meeting: rename it, copy or export it, write its summary, delete it, and read what was
/// said, a timestamped bubble per sentence, You on the right and Others on the left. Use on the
/// UI thread. What it shows is the user's own text.
/// </summary>
internal sealed class MeetingView : StackPanel, IDisposable
{
    private readonly MeetingActions _meetings;
    private readonly Palette _palette;
    private readonly Action _back;
    private readonly TextBox _title = new() { Width = 360, FontSize = 18, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _note = new() { Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _summary = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };
    private readonly Button _summarize = Small("Write summary");

    // Cancelled when the view is left or the meeting is deleted, so a summary in progress stops
    // instead of holding the language model, which dictation cleanup shares.
    private readonly CancellationTokenSource _closing = new();
    private MeetingRecord _meeting;

    public MeetingView(MeetingRecord meeting, MeetingActions meetings, Palette palette, Action back)
    {
        _meeting = meeting;
        _meetings = meetings;
        _palette = palette;
        _back = back;
        _title.Text = meeting.Title;
        _title.MaxLength = MeetingRecord.MaxTitleLength;
        _note.Foreground = MainWindow.Brush(palette.Muted);

        var backButton = Small("← Meetings");
        backButton.Click += (_, _) => back();
        var rename = Small("Rename");
        rename.Click += (_, _) => Rename();
        _title.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Rename();
            }
        };
        var copy = Small("Copy transcript");
        copy.Click += async (_, _) =>
        {
            try
            {
                var copied = await _meetings.CopyAsync(MeetingTranscript.ClipboardText(_meeting.Segments));
                _note.Text = copied ? "Copied." : "The clipboard is busy.";
            }
            catch (Exception e)
            {
                // From a click: anything not caught here would vanish silently.
                Log.Error(LogEvent.TranscriptCopyFailed, e);
            }
        };
        var export = Small("Export Markdown...");
        export.Click += (_, _) => Export();
        var delete = Small("Delete");
        delete.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show("Delete this meeting? This can't be undone.", "Naad",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _closing.Cancel();
                _meetings.Delete(_meeting);
                back();
            }
        };
        _summarize.Click += (_, _) => _ = SummarizeAsync();
        _summarize.IsEnabled = meetings.CanSummarize;

        Children.Add(Line(backButton));
        Children.Add(Line(_title, rename));
        Children.Add(new TextBlock
        {
            Text = _meeting.Started.LocalDateTime.ToString("f", System.Globalization.CultureInfo.CurrentCulture)
                + " · " + MeetingList.SummaryLine(_meeting),
            Foreground = MainWindow.Brush(palette.Muted),
        });
        Children.Add(Line(copy, export, _summarize, delete));
        Children.Add(_note);
        Children.Add(_summary);
        ShowSummary();
        foreach (var turn in MeetingTranscript.Turns(_meeting.Segments))
        {
            foreach (var sentence in MeetingList.Sentences(turn.Text))
            {
                Children.Add(Bubble(turn, sentence));
            }
        }
    }

    /// <summary>The view is left: a summary still being written stops.</summary>
    public void Dispose()
    {
        _closing.Cancel();
        _closing.Dispose();
    }

    private void Rename()
    {
        if (_meeting.Renamed(_title.Text) is null)
        {
            _title.Text = _meeting.Title;
            return;
        }

        if (_meetings.Rename(_meeting, _title.Text) is { } renamed)
        {
            _meeting = _meeting with { Title = renamed.Title };
            _note.Text = "Renamed.";
        }
        else
        {
            _note.Text = "The new title couldn't be saved.";
        }

        _title.Text = _meeting.Title;
    }

    private void ShowSummary()
    {
        _summary.Text = _meeting.Summary ?? "";
        _summarize.Content = _meeting.Summary is null ? "Write summary" : "Write summary again";
        if (!_meetings.CanSummarize)
        {
            _note.Text = "Set up a language model on the AI tab to write summaries.";
        }
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task SummarizeAsync()
    {
        _summarize.IsEnabled = false;
        _note.Text = "Writing the summary... A long meeting on this PC can take a few minutes.";
        try
        {
            if (await _meetings.SummarizeAsync(_meeting, _closing.Token) is { } summarized)
            {
                // Only the summary: a rename made meanwhile stays.
                _meeting = _meeting with { Summary = summarized.Summary };
                _note.Text = "";
                ShowSummary();
            }
            else
            {
                _note.Text = "The summary couldn't be written. Check the AI settings and try again.";
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The view closed or the meeting was deleted: nothing to show or save.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.MeetingSummaryFailed, e);
            _note.Text = "The summary couldn't be written.";
        }
        finally
        {
            _summarize.IsEnabled = _meetings.CanSummarize;
        }
    }

    // A file the user picks: the only way a meeting leaves Naad's encrypted store as plain text.
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = FileName(_meeting.Title) + ".md",
            Filter = "Markdown (*.md)|*.md",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, MeetingList.Markdown(_meeting, TimeZoneInfo.Local));
            Log.Info(LogEvent.MeetingExported);
            _note.Text = "Exported.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.MeetingExportFailed, e);
            _note.Text = "The file couldn't be written.";
        }
    }

    // The title without characters a file name can't hold, or "Meeting" when nothing is left.
    private static string FileName(string title) =>
        string.Concat(title.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.') is { Length: > 0 } name
            ? name
            : "Meeting";

    private Border Bubble(MeetingSegment turn, string sentence)
    {
        var you = turn.Speaker == Speaker.You;
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = $"{turn.Speaker} · {MeetingList.Clock(turn.Start)}",
            FontSize = 11,
            Foreground = MainWindow.Brush(_palette.Muted),
        });
        text.Children.Add(new TextBlock { Text = sentence, TextWrapping = TextWrapping.Wrap });
        return new Border
        {
            Child = text,
            MaxWidth = 460,
            HorizontalAlignment = you ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Background = MainWindow.Brush(you ? _palette.Border : _palette.Surface),
            CornerRadius = new CornerRadius(8),
        };
    }

    private static Button Small(string label) => new()
    {
        Content = label,
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(0, 0, 8, 0),
    };

    private static StackPanel Line(params UIElement[] children)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var child in children)
        {
            line.Children.Add(child);
        }

        return line;
    }
}
