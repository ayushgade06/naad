using Naad.Audio;
using Naad.Dictation;
using Naad.Native;

namespace Naad.Tray;

/// <summary>
/// A small bar near the bottom of the screen showing that Naad is listening, with a level
/// meter, or transcribing. It must never become the foreground window: the focus guard refuses
/// to paste once the foreground window changes, so an overlay that took focus would block every
/// dictation it shows. It never activates, takes no keyboard input, lets clicks through, and
/// stays out of Alt+Tab and the taskbar. Create, use and dispose it on the UI thread.
/// </summary>
internal sealed class RecordingOverlay : Form
{
    /// <summary>
    /// Never activated (WS_EX_NOACTIVATE), above other windows (WS_EX_TOPMOST), not in Alt+Tab or
    /// the taskbar (WS_EX_TOOLWINDOW), and click-through (WS_EX_TRANSPARENT).
    /// </summary>
    public const int ExtendedStyles =
        User32.WsExNoActivate | User32.WsExTopmost | User32.WsExToolWindow | User32.WsExTransparent;

    // Slightly see-through. Opacity below 1 also makes the window layered, and Windows only lets
    // clicks pass through a WS_EX_TRANSPARENT window when it is layered.
    private const double SeeThrough = 0.9;

    // Sizes at 96 DPI: big enough to read at a glance, small enough to cover little.
    private const int LogicalWidth = 220;
    private const int LogicalHeight = 40;
    private const int LogicalBottomMargin = 72;
    private const int LogicalPadding = 12;
    private const int LogicalBarWidth = 90;
    private const int LogicalBarHeight = 8;

    private static readonly Color Background = Color.FromArgb(0x20, 0x20, 0x20);
    private static readonly Color Track = Color.FromArgb(0x50, 0x50, 0x50);

    // The tray icon's listening red, so the two read as one signal.
    private static readonly Color LevelColor = Color.FromArgb(0xD3, 0x2F, 0x2F);

    private readonly LevelMeter _meter = new();
    private DictationState _state;

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Background;
        ForeColor = Color.White;
        Opacity = SeeThrough;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= ExtendedStyles;
            return parameters;
        }
    }

    /// <summary>
    /// Shows the dictation state on the monitor of <paramref name="target"/> (the window being
    /// dictated into), or hides when idle.
    /// </summary>
    public void ShowState(DictationState state, nint target)
    {
        _state = state;
        if (state == DictationState.Idle)
        {
            Hide();
            return;
        }

        if (state == DictationState.Recording)
        {
            _meter.Reset();
            Place(target);
        }

        // ShowWithoutActivation makes this SW_SHOWNOACTIVATE: focus stays where it is.
        Show();
        Invalidate();
    }

    /// <summary>Adds a microphone level from <see cref="AudioRecorder.LevelMeasured"/>, marshalled here.</summary>
    public void ShowLevel(double rms, TimeSpan duration)
    {
        if (_state == DictationState.Recording && Visible)
        {
            _meter.Add(rms, duration);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var padding = Scale(LogicalPadding);
        var text = _state == DictationState.Recording ? "Listening" : "Transcribing";
        TextRenderer.DrawText(
            e.Graphics,
            text,
            Font,
            new Rectangle(padding, 0, Width - (2 * padding), Height),
            ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if (_state != DictationState.Recording)
        {
            return;
        }

        var bar = new Rectangle(
            Width - padding - Scale(LogicalBarWidth),
            (Height - Scale(LogicalBarHeight)) / 2,
            Scale(LogicalBarWidth),
            Scale(LogicalBarHeight));
        using var track = new SolidBrush(Track);
        using var level = new SolidBrush(LevelColor);
        e.Graphics.FillRectangle(track, bar);
        e.Graphics.FillRectangle(level, bar with { Width = (int)Math.Round(bar.Width * _meter.Level) });
    }

    // Centred near the bottom of the target's monitor, where it is seen without covering the
    // text being dictated, which is usually higher up.
    // ponytail: sized for the DPI Naad's window has when shown; on a second monitor with another
    // scale it may look a little large or small. Per-monitor sizing via DpiChanged if users notice.
    private void Place(nint target)
    {
        var screen = target == 0 ? Screen.PrimaryScreen! : Screen.FromHandle(target);
        var area = screen.WorkingArea;
        var size = new Size(Scale(LogicalWidth), Scale(LogicalHeight));
        Bounds = new Rectangle(
            area.Left + ((area.Width - size.Width) / 2),
            area.Bottom - size.Height - Scale(LogicalBottomMargin),
            size.Width,
            size.Height);
    }

    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);
}
