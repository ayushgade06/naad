using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Naad.Meetings;
using Orientation = System.Windows.Controls.Orientation;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace Naad.Window;

/// <summary>
/// The live meeting card: elapsed time, a level meter, a banner when the microphone is blocked,
/// and the lines as they are transcribed, then "Finishing" while the last pieces are. It reads the
/// recorder ten times a second while shown, so no capture thread ever touches the page. The lines
/// are the user's own words, shown only here. Use on the UI thread; dispose to stop the timer.
/// </summary>
internal sealed class MeetingCardView : Border, IDisposable
{
    // Ten updates a second keeps the meter lively without noticeable work.
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    // The same red as the tray icon's recording dot: it reads on light and dark themes alike.
    private const uint Red = 0xFFD32F2F;

    // Room for a few turns; the rest scrolls.
    private const double LinesHeight = 220;

    private readonly MeetingRecorder _recorder;
    private readonly Palette _palette;
    private readonly DispatcherTimer _timer = new() { Interval = Tick };
    private readonly TextBlock _clock = new()
    {
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 12, 0),
    };
    private readonly ProgressBar _level = new() { Width = 120, Height = 6, Maximum = 1 };
    private readonly TextBlock _banner = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly StackPanel _lines = new();
    private readonly ScrollViewer _scroll;
    private int _shown = -1;
    private string? _shownPlaceholder;
    private readonly TextBlock _dot = new() { Text = "● " };

    public MeetingCardView(MeetingRecorder recorder, Palette palette)
    {
        _recorder = recorder;
        _palette = palette;
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        _dot.Foreground = MainWindow.Brush(Red);
        top.Children.Add(_dot);
        top.Children.Add(_clock);
        top.Children.Add(_level);
        _level.VerticalAlignment = VerticalAlignment.Center;
        _scroll = new ScrollViewer
        {
            Content = _lines,
            MaxHeight = LinesHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_banner);
        stack.Children.Add(_scroll);
        Child = stack;
        Padding = new Thickness(12);
        Margin = new Thickness(0, 0, 0, 12);
        Background = MainWindow.Brush(palette.Surface);
        BorderBrush = MainWindow.Brush(palette.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6);
        _banner.Foreground = MainWindow.Brush(Red);
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
    }

    public void Dispose() => _timer.Stop();

    private void Update()
    {
        var count = _recorder.LineCount;
        var state = MeetingCard.Describe(
            _recorder.IsRecording, _recorder.Finishing is not null, _recorder.MicrophoneIsBlocked, _recorder.Elapsed,
            count);
        Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
        if (!state.Visible)
        {
            _shown = -1;
            return;
        }

        _clock.Text = state.Clock;
        _level.Value = state.Finishing ? 0 : _recorder.Level;
        _banner.Text = state.Banner ?? "";
        _banner.Visibility = state.Banner is null ? Visibility.Collapsed : Visibility.Visible;
        _banner.Foreground = MainWindow.Brush(state.Finishing ? _palette.Muted : Red);
        _dot.Visibility = state.Finishing ? Visibility.Hidden : Visibility.Visible;
        // Also when only the placeholder changed, as when Listening gives way to Finishing.
        if (count == _shown && state.Placeholder == _shownPlaceholder)
        {
            return;
        }

        // Rebuilt only when a line arrives; turns of one speaker read as one.
        _shown = count;
        _shownPlaceholder = state.Placeholder;
        _lines.Children.Clear();
        if (state.Placeholder is { } placeholder)
        {
            _lines.Children.Add(Line(placeholder, muted: true));
            return;
        }

        foreach (var turn in MeetingTranscript.Turns(_recorder.Lines))
        {
            var failed = turn.Text.Contains(MeetingSegment.Failed, StringComparison.Ordinal);
            _lines.Children.Add(Line($"{turn.Speaker}: {turn.Text}", failed));
        }

        _scroll.ScrollToEnd();
    }

    private TextBlock Line(string text, bool muted) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4),
        Foreground = MainWindow.Brush(muted ? _palette.Muted : _palette.Text),
    };
}
