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
/// keyboard input, lets clicks through, and stays out of Alt+Tab and the taskbar; only its stop
/// button, and an update notice, take clicks, through an <see cref="OverlayClickTarget"/> that
/// never activates either. Create, use and dispose it on the UI thread.
/// </summary>
internal sealed partial class RecordingOverlay : Form
{
    /// <summary>
    /// Never activated (WS_EX_NOACTIVATE), above other windows (WS_EX_TOPMOST), not in Alt+Tab or
    /// the taskbar (WS_EX_TOOLWINDOW), click-through (WS_EX_TRANSPARENT), and drawn from a bitmap
    /// with per-pixel alpha (WS_EX_LAYERED), which gives the pill smooth rounded edges. Windows
    /// only lets clicks pass through a WS_EX_TRANSPARENT window when it is layered.
    /// </summary>
    public const int ExtendedStyles = User32.WsExNoActivate | User32.WsExTopmost | User32.WsExToolWindow
        | User32.WsExTransparent | User32.WsExLayered;

    // Sizes at 96 DPI; the overlay's own size comes from OverlayLayout.
    private const int LogicalHeight = 36;
    private const int LogicalBottomMargin = 72;
    private const int LogicalBarWidth = 3;
    private const int LogicalBarGap = 3;
    private const int LogicalMaxBarHeight = 20;
    private const int BarCount = 15;

    // With live preview a line of about ten words shows above the bars.
    private const int LogicalLiveTextHeight = 24;
    private const int LogicalTextPadding = 14;

    // The words' line sits this far below the pill's top edge, clear of its rounded corner.
    private const int LogicalLiveTextTop = 6;
    private const string Ellipsis = "…";

    // Long enough to read two words at a glance, short enough not to linger over the text.
    private const int FallbackNoticeMilliseconds = 2500;

    // A notice that can be clicked stays long enough to reach it with the mouse.
    private const int ClickableNoticeMilliseconds = 7000;

    // A microphone that opens sooner than this shows the bars straight away: a "Starting" that
    // flashes for a moment reads as a glitch.
    private const int StartingDelayMilliseconds = 200;

    private static readonly Color Background = Color.FromArgb(0x14, 0x14, 0x16);
    private static readonly Color Border = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly Color BarColor = Color.FromArgb(0xF5, 0xF5, 0xF7);
    private static readonly Color TextColor = Color.FromArgb(0xC8, 0xC8, 0xCC);

    private readonly LevelMeter _meter = new();
    private readonly VoiceBars _bars = new(BarCount);
    private readonly System.Windows.Forms.Timer _notice = new() { Interval = FallbackNoticeMilliseconds };
    private readonly System.Windows.Forms.Timer _starting = new() { Interval = StartingDelayMilliseconds };
    private readonly float[] _shown = new float[BarCount];
    private DictationState _state;
    private string _label = "Transcribing";
    private bool _microphoneOpen;
    private long _recordingStarted;

    private readonly OverlayClickTarget _click = new();
    private bool _showTimer;
    private OverlayStyle _style;
    private string? _monitor;
    private string _liveText = "";
    private Rectangle _area;

    // Hands-free: the stop button shows. Editing: the recording is an Edit Mode instruction.
    private bool _handsFree;
    private bool _editing;
    private Action? _stop;

    public RecordingOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        _starting.Tick += (_, _) =>
        {
            _starting.Stop();
            if (_state == DictationState.Recording && !_microphoneOpen)
            {
                ShowNow();
            }
        };
        _notice.Tick += (_, _) =>
        {
            _notice.Stop();
            _click.Clear();
            if (_state == DictationState.Idle)
            {
                Hide();
            }
            else if (Visible)
            {
                Render();
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
        _starting.Stop();
        if (state != DictationState.Recording && _handsFree)
        {
            _handsFree = false;
            _click.Clear();
        }

        if (state == DictationState.Idle)
        {
            // The words were this dictation's: a notice shown next must not show them again.
            _liveText = "";
            _editing = false;

            // A notice that cleanup fell back stays up for its time.
            if (!_notice.Enabled)
            {
                Hide();
            }
            else if (Visible)
            {
                Render();
            }

            return;
        }

        if (state == DictationState.Recording)
        {
            _notice.Stop();
            _click.Clear();
            _handsFree = false;
            _editing = false;
            _label = "Transcribing";
            _microphoneOpen = false;
            _recordingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            _liveText = "";
            Array.Clear(_shown);
            _meter.Reset();
            _bars.Reset();
            Place(target);

            // Shown once the microphone opens, or as "Starting" if that takes a while.
            Hide();
            _starting.Start();
            return;
        }

        ShowNow();
    }

    // ShowWithoutActivation makes this SW_SHOWNOACTIVATE: focus stays where it is.
    private void ShowNow()
    {
        Show();
        Render();
    }

    /// <summary>
    /// Shows the words heard so far above the bars, the newest at the right; the pill grows to fit
    /// a line of them. User content: drawn only, never kept beyond this dictation.
    /// </summary>
    public void ShowLiveText(string text)
    {
        if (_state == DictationState.Idle || !Visible || text.Length == 0)
        {
            return;
        }

        _liveText = text;
        Render();
    }

    /// <summary>
    /// Uses these choices from the next recording on: a timer at the pill's right end, the style,
    /// and the monitor (null follows the active window; see <see cref="OverlayLayout.PickArea"/>).
    /// </summary>
    public void UseOptions(bool timer, OverlayStyle style, string? monitor) =>
        (_showTimer, _style, _monitor) = (timer, style, monitor);

    /// <summary>What the stop button does: stop the hands-free dictation.</summary>
    public void UseStop(Action stop) => _stop = stop;

    /// <summary>The recording runs hands-free: the stop button shows and takes clicks.</summary>
    public void ShowHandsFree()
    {
        _handsFree = true;
        if (Visible)
        {
            Render();
        }
    }

    /// <summary>The recording is an Edit Mode instruction: it gets the editing look.</summary>
    public void ShowEditing()
    {
        _editing = true;
        if (Visible)
        {
            Render();
        }
    }

    /// <summary>The language model is tidying the transcript, or rewriting the selection.</summary>
    public void ShowCleaningUp(bool editing) => ShowLabel(editing ? "Editing" : "Cleaning up");

    /// <summary>The microphone is recording: the bars replace "Starting".</summary>
    public void ShowMicrophoneOpen()
    {
        _microphoneOpen = true;
        _starting.Stop();

        // The timer counts what is recorded, not the wait for the microphone.
        _recordingStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_state == DictationState.Recording)
        {
            ShowNow();
        }
    }

    /// <summary>
    /// Cleanup failed, so the transcript is pasted as said: says so for a moment, even after the
    /// dictation ends.
    /// </summary>
    public void ShowCleanupFellBack() => ShowNotice("Pasted as said");

    /// <summary>Whether a notice such as "Pasted as said" is on screen now.</summary>
    public bool ShowingNotice => _notice.Enabled;

    /// <summary>
    /// Says <paramref name="text"/> (two or three words) for a moment, even after the dictation.
    /// With <paramref name="clicked"/>, it stays longer and clicking it calls that.
    /// </summary>
    public void ShowNotice(string text, Action? clicked = null)
    {
        // Started first: the size and the drawing both depend on whether a notice shows.
        _notice.Stop();
        _notice.Interval = clicked is null ? FallbackNoticeMilliseconds : ClickableNoticeMilliseconds;
        _notice.Start();
        _click.Clear();
        ShowLabel(text);
        if (!Visible)
        {
            Show();
            Render();
        }

        if (clicked is not null)
        {
            _click.ShowAt(Bounds, clicked);
        }
    }

    /// <summary>Adds a microphone level from <see cref="AudioRecorder.LevelMeasured"/>, marshalled here.</summary>
    public void ShowLevel(double rms, TimeSpan duration)
    {
        if (_state == DictationState.Recording && Visible)
        {
            // Quiet buffers keep coming too, so the bars breathe at the buffers' pace.
            _bars.Push(_meter.Add(rms, duration));
            Render();
        }
    }

    // A layered window shows only what UpdateLayeredWindow gives it, so OnPaint never runs:
    // each change draws the whole pill into a bitmap and hands that over.
    private void Render()
    {
        Fit();
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            Draw(graphics);
        }

        // Each handle is taken inside the try, so one that fails never leaks the ones before it.
        nint screen = 0, memory = 0, handle = 0, previous = 0;
        try
        {
            screen = User32.GetDC(0);
            memory = Gdi32.CreateCompatibleDC(screen);
            handle = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = Gdi32.SelectObject(memory, handle);
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
            if (previous != 0)
            {
                Gdi32.SelectObject(memory, previous);
            }

            if (handle != 0)
            {
                Gdi32.DeleteObject(handle);
            }

            if (memory != 0)
            {
                Gdi32.DeleteDC(memory);
            }

            if (screen != 0)
            {
                _ = User32.ReleaseDC(0, screen);
            }
        }

        // The button sits over the drawn stop glyph, which needs the size just fitted.
        if (_handsFree && _state == DictationState.Recording && _microphoneOpen && !_notice.Enabled
            && _stop is { } stop)
        {
            _click.ShowAt(OverlayLayout.StopButton(Bounds, DeviceDpi / 96.0), stop);
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
            _starting.Dispose();
            _click.Dispose();
        }

        base.Dispose(disposing);
    }

    // Centred near the bottom of the target's monitor, where it is seen without covering the
    // text being dictated, which is usually higher up.
    // ponytail: sized for the DPI Naad's window has when shown; on a second monitor with another
    // scale it may look a little large or small. Per-monitor sizing via DpiChanged if users notice.
    private void Place(nint target)
    {
        var screens = Screen.AllScreens.Select(s => new OverlayScreen(s.DeviceName, s.WorkingArea, s.Primary)).ToList();
        _area = OverlayLayout.PickArea(screens, target == 0 ? null : Screen.FromHandle(target).DeviceName, _monitor);
    }

    // The size for what it shows now, centred on the bottom of the area chosen by Place.
    private void Fit()
    {
        var words = _notice.Enabled || _state != DictationState.Recording || !_microphoneOpen;
        var logical = OverlayLayout.Size(
            _style, words, _liveText.Length > 0, _handsFree && _state == DictationState.Recording);
        var size = new Size(Scale(logical.Width), Scale(logical.Height));
        var bounds = new Rectangle(
            _area.Left + ((_area.Width - size.Width) / 2),
            _area.Bottom - size.Height - Scale(LogicalBottomMargin),
            size.Width,
            size.Height);
        if (Bounds != bounds)
        {
            Bounds = bounds;
        }
    }

    private int Scale(int logical) => (int)Math.Round(logical * DeviceDpi / 96.0);
}
