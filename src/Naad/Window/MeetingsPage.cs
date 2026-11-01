using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Naad.Meetings;
using Button = System.Windows.Controls.Button;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace Naad.Window;

/// <summary>
/// The Meetings page: start and stop meeting notes, and the saved meetings by day, searchable,
/// each opening in <see cref="MeetingView"/>. What it shows is the user's own text, decrypted only
/// to show it. Use on the UI thread.
/// </summary>
internal sealed class MeetingsPage : DockPanel, IDisposable
{
    private readonly MeetingActions _meetings;
    private readonly Palette _palette;
    private readonly Button _toggle = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBox _search = new() { Width = 280, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Button _deleteAll = Small("Delete all meetings");
    private readonly ScrollViewer _content = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    // Decrypted once per reload, so typing in Search filters in memory.
    private IReadOnlyList<MeetingRecord> _all = [];

    public MeetingsPage(MeetingActions meetings, Palette palette)
    {
        _meetings = meetings;
        _palette = palette;
        var top = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock
        {
            Text = "Meeting notes",
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
        });
        top.Children.Add(new TextBlock
        {
            Text = "Records your microphone as You and the PC's sound as Others, and transcribes it in pieces " +
                "while the meeting runs. When you stop, the transcript is saved encrypted and copied to the " +
                "clipboard. Tell the others on the call that you are taking notes.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = MainWindow.Brush(palette.Muted),
            Margin = new Thickness(0, 0, 0, 12),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        row.Children.Add(_toggle);
        row.Children.Add(_status);
        top.Children.Add(row);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(new TextBlock { Text = "Search ", VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_search);
        tools.Children.Add(_deleteAll);
        top.Children.Add(tools);
        SetDock(top, Dock.Top);
        Children.Add(top);
        Children.Add(_content);

        _toggle.Click += (_, _) =>
        {
            _toggle.IsEnabled = false;
            _status.Text = _meetings.Recorder.IsRecording ? "Finishing the last pieces..." : "Starting...";
            _ = _meetings.ToggleAsync();
        };
        _deleteAll.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(
                "Delete every saved meeting and its recordings? This can't be undone.", "Naad",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _meetings.DeleteAll();
                Reload();
            }
        };
        _search.TextChanged += (_, _) => ShowList();
        _meetings.Recorder.RecordingChanged += OnRecordingChanged;
        _meetings.Finished += OnFinished;
        OnRecordingChanged(_meetings.Recorder.IsRecording);
        Reload();
    }

    public void Dispose()
    {
        (_content.Content as IDisposable)?.Dispose();
        _meetings.Recorder.RecordingChanged -= OnRecordingChanged;
        _meetings.Finished -= OnFinished;
    }

    private void OnRecordingChanged(bool recording)
    {
        var finishing = !recording && _meetings.Recorder.IsBusy;
        _toggle.Content = recording ? "Stop and copy" : "Start meeting notes";
        _toggle.IsEnabled = !finishing;
        _status.Text = recording ? "Recording" : finishing ? "Finishing the last pieces..." : "";
    }

    private void OnFinished(MeetingOutcome outcome)
    {
        OnRecordingChanged(_meetings.Recorder.IsRecording);
        _status.Text = Tray.TrayIcon.MeetingMessage(outcome) ?? _status.Text;

        // A meeting open in its view stays open; the list shows the new one.
        if (_content.Content is not MeetingView)
        {
            Reload();
        }
    }

    // Decrypts every meeting; a few hundred take a moment at most.
    private void Reload()
    {
        _all = _meetings.Store.All();
        ShowList();
    }

    private void ShowList()
    {
        var all = _all;
        _deleteAll.IsEnabled = all.Count > 0;
        var list = new StackPanel();
        var shown = all.Where(m => MeetingList.Matches(m, _search.Text)).ToList();
        if (shown.Count == 0)
        {
            list.Children.Add(Muted(all.Count == 0 ? "No meetings saved yet." : "No meeting matches."));
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (day, meetings) in MeetingList.ByDay(shown, today, TimeZoneInfo.Local))
        {
            list.Children.Add(new TextBlock
            {
                Text = day,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 4),
            });
            foreach (var meeting in meetings)
            {
                list.Children.Add(Row(meeting));
            }
        }

        (_content.Content as IDisposable)?.Dispose();
        _content.Content = list;
    }

    private Border Row(MeetingRecord meeting)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = meeting.Title, FontWeight = FontWeights.SemiBold });
        text.Children.Add(Muted(
            meeting.Started.LocalDateTime.ToString("h:mmtt", CultureInfo.InvariantCulture).ToLowerInvariant()
            + " · " + MeetingList.SummaryLine(meeting)));
        var open = Small("Open");
        open.Click += (_, _) => _content.Content = new MeetingView(meeting, _meetings, _palette, Reload);
        var copy = Small("Copy");
        copy.Click += async (_, _) =>
        {
            try
            {
                await _meetings.CopyAsync(MeetingTranscript.ClipboardText(meeting.Segments));
            }
            catch (Exception e)
            {
                // From a click: anything not caught here would vanish silently.
                Log.Error(LogEvent.TranscriptCopyFailed, e);
            }
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(open);
        buttons.Children.Add(copy);
        var row = new DockPanel();
        SetDock(buttons, Dock.Right);
        row.Children.Add(buttons);
        row.Children.Add(text);
        return new Border
        {
            Child = row,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Background = MainWindow.Brush(_palette.Surface),
            BorderBrush = MainWindow.Brush(_palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
    }

    private TextBlock Muted(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = MainWindow.Brush(_palette.Muted),
    };

    private static Button Small(string label) => new()
    {
        Content = label,
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(8, 0, 0, 0),
    };
}
