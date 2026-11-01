using System.Windows;
using System.Windows.Controls;
using Naad.Meetings;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace Naad.Window;

/// <summary>
/// The Meetings page: start and stop meeting notes, how many are saved, and Delete all. Use on
/// the UI thread.
/// </summary>
internal sealed class MeetingsPage : DockPanel, IDisposable
{
    private readonly MeetingActions _meetings;
    private readonly Button _toggle = new() { Padding = new Thickness(12, 4, 12, 4) };
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly TextBlock _saved = new() { Margin = new Thickness(0, 16, 0, 8) };
    private readonly Button _deleteAll = new()
    {
        Content = "Delete all meetings",
        Padding = new Thickness(10, 2, 10, 2),
    };

    public MeetingsPage(MeetingActions meetings, Palette palette)
    {
        _meetings = meetings;
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
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_toggle);
        row.Children.Add(_status);
        top.Children.Add(row);
        top.Children.Add(_saved);
        top.Children.Add(_deleteAll);
        _deleteAll.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        _deleteAll.Click += (_, _) =>
        {
            if (System.Windows.MessageBox.Show(
                "Delete every saved meeting and its recordings? This can't be undone.", "Naad",
                MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                _meetings.DeleteAll();
                ShowSaved();
            }
        };
        SetDock(top, Dock.Top);
        Children.Add(top);
        _toggle.Click += (_, _) =>
        {
            _toggle.IsEnabled = false;
            _status.Text = _meetings.Recorder.IsRecording ? "Finishing the last pieces..." : "Starting...";
            _ = _meetings.ToggleAsync();
        };
        _meetings.Recorder.RecordingChanged += OnRecordingChanged;
        _meetings.Finished += OnFinished;
        OnRecordingChanged(_meetings.Recorder.IsRecording);
        ShowSaved();
    }

    public void Dispose()
    {
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

    // Decrypts every meeting to count them; few enough that it is quick.
    private void ShowSaved()
    {
        var count = _meetings.Store.All().Count;
        _saved.Text = count switch
        {
            0 => "No meetings saved.",
            1 => "1 meeting saved.",
            _ => $"{count} meetings saved.",
        };
        _deleteAll.IsEnabled = count > 0;
    }

    private void OnFinished(MeetingOutcome outcome)
    {
        OnRecordingChanged(_meetings.Recorder.IsRecording);
        _status.Text = Tray.TrayIcon.MeetingMessage(outcome) ?? _status.Text;
        ShowSaved();
    }
}
