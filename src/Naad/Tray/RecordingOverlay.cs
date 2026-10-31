using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Naad.Audio;
using Naad.Dictation;
using Naad.Native;

namespace Naad.Tray;

/// <summary>
/// A small dark pill near the bottom of the screen showing that Naad is listening, with voice
/// bars that rise and fall with the microphone level, or transcribing. It must never become the
/// foreground window: the focus guard refuses to paste once the foreground window changes, so an
/// overlay that took focus would block every dictation it shows. It never activates, takes no
/// keyboard input, lets clicks through, and stays out of Alt+Tab and the taskbar. Create, use and
/// dispose it on the UI thread.
/// </summary>
internal sealed class RecordingOverlay : Form
{
    /// <summary>
    /// Never activated (WS_EX_NOACTIVATE), above other windows (WS_EX_TOPMOST), not in Alt+Tab or
    /// the taskbar (WS_EX_TOOLWINDOW), click-through (WS_EX_TRANSPARENT), and drawn from a bitmap
    /// with per-pixel alpha (WS_EX_LAYERED), which gives the pill smooth rounded edges. Windows
    /// only lets clicks pass through a WS_EX_TRANSPARENT window when it is layered.
    /// </summary>
    public const int ExtendedStyles = User32.WsExNoActivate | User32.WsExTopmost | User32.WsExToolWindow
        | User32.WsExTransparent | User32.WsExLayered;

    // Sizes at 96 DPI: big enough to read at a glance, small enough to cover little.
    private const int LogicalWidth = 132;
    private const int LogicalHeight = 36;
    private const int LogicalBottomMargin = 72;
    private const int LogicalBarWidth = 3;
    private const int LogicalBarGap = 3;
    private const int LogicalMaxBarHeight = 20;
    private const int BarCount = 15;

    // Long enough to read two words at a glance, short enough not to linger over the text.
    private const int FallbackNoticeMilliseconds = 2500;

    private static readonly Color Background = Color.FromArgb(0x14, 0x14, 0x16);
    private static readonly Color Border = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Color BarColor = Color.FromArgb(0xF5, 0xF5, 0xF7);
    private static readonly Color TextColor = Color.FromArgb(0xC8, 0xC8, 0xCC);

    private readonly LevelMeter _meter = new();
    private readonly VoiceBars _bars = new(BarCount);
    private readonly System.Windows.Forms.Timer _notice = new() { Interval = FallbackNoticeMilliseconds };
    private DictationState _state;
    private string _label = "Transcribing";

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _notice.Tick += (_, _) =>
        {
            _notice.Stop();
            if (_state == DictationState.Idle)
            {
                Hide();
            }
        };
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
            // A notice that cleanup fell back stays up for its time.
            if (!_notice.Enabled)
            {
                Hide();
            }

            return;
        }

        if (state == DictationState.Recording)
        {
            _notice.Stop();
            _label = "Transcribing";
            _meter.Reset();
            _bars.Reset();
            Place(target);
        }

        // ShowWithoutActivation makes this SW_SHOWNOACTIVATE: focus stays where it is.
        Show();
        Render();
    }

    /// <summary>The language model is tidying the transcript.</summary>
    public void ShowCleaningUp() => ShowLabel("Cleaning up");

    /// <summary>
    /// Cleanup failed, so the transcript is pasted as said: says so for a moment, even after the
    /// dictation ends.
    /// </summary>
    public void ShowCleanupFellBack()
    {
        ShowLabel("Pasted as said");
        _notice.Start();
    }

    /// <summary>Adds a microphone level from <see cref="AudioRecorder.LevelMeasured"/>, marshalled here.</summary>
    public void ShowLevel(double rms, TimeSpan duration)
    {
        if (_state == DictationState.Recording && Visible)
        {
            _bars.Push(_meter.Add(rms, duration));
            Render();
        }
    }

    // A layered window shows only what UpdateLayeredWindow gives it, so OnPaint never runs:
    // each change draws the whole pill into a bitmap and hands that over.
    private void Render()
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Draw(graphics);
        }

        var screen = User32.GetDC(0);
        var memory = Gdi32.CreateCompatibleDC(screen);
        var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = Gdi32.SelectObject(memory, handle);
        try
        {
            User32.UpdateLayeredWindow(
                Handle,
                screen,
                new User32.Point(Left, Top),
                new User32.Size(Width, Height),
                memory,
                new User32.Point(0, 0),
                0,
                new User32.BlendFunction(User32.AcSrcOver, 0, 255, User32.AcSrcAlpha),
                User32.UlwAlpha);
        }
        finally
        {
            Gdi32.SelectObject(memory, previous);
            Gdi32.DeleteObject(handle);
            Gdi32.DeleteDC(memory);
            _ = User32.ReleaseDC(0, screen);
        }
    }

    private void Draw(Graphics graphics)
    {
        using (var pill = Capsule(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1)))
        using (var fill = new SolidBrush(Background))
        using (var edge = new Pen(Border, 1))
        {
            graphics.FillPath(fill, pill);
            graphics.DrawPath(edge, pill);
        }

        if (_state != DictationState.Recording)
        {
            using var text = new SolidBrush(TextColor);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString(_label, Font, text, new RectangleF(0, 0, Width, Height), format);
            return;
        }

        // Each bar is a capsule never shorter than it is wide, so silence shows a row of dots.
        float barWidth = Scale(LogicalBarWidth);
        float step = barWidth + Scale(LogicalBarGap);
        var left = (Width - ((BarCount * step) - Scale(LogicalBarGap))) / 2f;
        using var brush = new SolidBrush(BarColor);
        for (var i = 0; i < BarCount; i++)
        {
            var height = Math.Max(barWidth, (float)(_bars.Height(i) * Scale(LogicalMaxBarHeight)));
            using var bar = Capsule(new RectangleF(left + (i * step), (Height - height) / 2f, barWidth, height));
            graphics.FillPath(brush, bar);
        }
    }

    private void ShowLabel(string label)
    {
        _label = label;
        if (Visible)
        {
            Render();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notice.Dispose();
        }

        base.Dispose(disposing);
    }

    // A rectangle with fully rounded ends: a pill when wide, a dot when square.
    private static GraphicsPath Capsule(RectangleF bounds)
    {
        var diameter = Math.Min(bounds.Width, bounds.Height);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
