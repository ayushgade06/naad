using Naad.Native;

namespace Naad.Tray;

/// <summary>
/// The offer of meeting notes: a small panel at the bottom of the screen with Take notes and Maybe
/// later. It never takes the focus, so the call app keeps it, and it fades away on its own. Only
/// its Take notes starts recording. Use on the UI thread.
/// </summary>
internal sealed class MeetingPrompt : Form
{
    // Sizes at 96 DPI: room for the question, a line of text and two buttons.
    private const int PanelWidth = 380;
    private const int PanelHeight = 132;
    private const int BottomGap = 96;
    private const int Pad = 14;
    private const int TextHeight = 40;
    private const int ChoiceWide = 10;
    private const int ChoiceTall = 5;

    // A quiet fade in and out.
    private const int FadeInMilliseconds = 250;
    private const int FadeOutMilliseconds = 200;
    private const int FadeStepMilliseconds = 25;

    // Dark, like Naad's other floating windows, with the Windows accent blue for the main choice.
    private static readonly Color Back = Color.FromArgb(0x1C, 0x1C, 0x1E);
    private static readonly Color Edge = Color.FromArgb(0x48, 0x48, 0x4A);
    private static readonly Color Accent = Color.FromArgb(0x0A, 0x84, 0xFF);

    private readonly System.Windows.Forms.Timer _fade = new() { Interval = FadeStepMilliseconds };
    private readonly System.Windows.Forms.Timer _dismiss = new() { Interval = (int)ShownFor.TotalMilliseconds };
    private readonly Label _text = new() { AutoSize = false, ForeColor = Color.Gainsboro };
    private Action? _takeNotes;
    private double _step;

    /// <summary>
    /// How long an offer stays: long enough to notice during a call's first minute; Naad offers
    /// nothing else meanwhile.
    /// </summary>
    public static readonly TimeSpan ShownFor = TimeSpan.FromSeconds(20);

    public MeetingPrompt()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Back;
        ForeColor = Color.White;
        Opacity = 0;
        Font = new Font("Segoe UI", 9.75f);
        var scale = DeviceDpi / 96.0;
        Size = new Size((int)(PanelWidth * scale), (int)(PanelHeight * scale));
        var pad = (int)(Pad * scale);
        var title = new Label
        {
            Text = "Want notes for this meeting?",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(pad, pad),
        };
        _text.Bounds = new Rectangle(pad, title.Bottom + (pad / 2), Width - (2 * pad), (int)(TextHeight * scale));
        var choicePadding = new Padding((int)(ChoiceWide * scale), (int)(ChoiceTall * scale), (int)(ChoiceWide * scale),
            (int)(ChoiceTall * scale));
        var later = Choice("Maybe later", Edge, choicePadding, Hide);
        var take = Choice("Take notes", Accent, choicePadding, () =>
        {
            var start = _takeNotes;
            Hide();
            start?.Invoke();
        });
        take.Location = new Point(Width - pad - take.Width, Height - pad - take.Height);
        later.Location = new Point(take.Left - (pad / 2) - later.Width, take.Top);
        Controls.AddRange([title, _text, later, take]);
        Paint += (_, e) =>
        {
            using var pen = new Pen(Edge);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
        _fade.Tick += (_, _) => Fade();
        _dismiss.Tick += (_, _) => FadeOut();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= User32.WsExNoActivate | User32.WsExTopmost | User32.WsExToolWindow;
            return parameters;
        }
    }

    /// <summary>Where the panel goes: centred, <paramref name="gap"/> above the bottom of the working area.</summary>
    public static Point Place(Rectangle workingArea, Size size, int gap) =>
        new(workingArea.Left + ((workingArea.Width - size.Width) / 2), workingArea.Bottom - gap - size.Height);

    /// <summary>
    /// Offers notes for a call in <paramref name="app"/> (a display name); Take notes calls
    /// <paramref name="takeNotes"/>. A newer offer replaces one still shown.
    /// </summary>
    public void Offer(string app, Action takeNotes)
    {
        _takeNotes = takeNotes;
        _text.Text = $"{app} is using the microphone. Naad can transcribe the call and copy the notes when " +
            "you stop.";
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        Location = Place(area, Size, (int)(BottomGap * DeviceDpi / 96.0));
        Opacity = 0;
        _step = FadeStepMilliseconds / (double)FadeInMilliseconds;
        Show();
        _fade.Start();
        _dismiss.Stop();
        _dismiss.Start();
    }

    /// <summary>Takes the offer away at once, as when notes start some other way.</summary>
    public new void Hide()
    {
        _fade.Stop();
        _dismiss.Stop();
        _takeNotes = null;
        base.Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fade.Dispose();
            _dismiss.Dispose();
        }

        base.Dispose(disposing);
    }

    // A click would normally activate the window it lands on; this one answers "don't".
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == User32.WmMouseActivate)
        {
            m.Result = User32.MaNoActivate;
            return;
        }

        base.WndProc(ref m);
    }

    private void FadeOut()
    {
        _dismiss.Stop();
        _step = -FadeStepMilliseconds / (double)FadeOutMilliseconds;
        _fade.Start();
    }

    private void Fade()
    {
        var next = Math.Clamp(Opacity + _step, 0, 1);
        Opacity = next;
        if (next >= 1 || next <= 0)
        {
            _fade.Stop();
            if (next <= 0)
            {
                Hide();
            }
        }
    }

    // A label, not a button: a button would take the focus when clicked, and the call app needs it.
    private static Label Choice(string text, Color back, Padding padding, Action click)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            BackColor = back,
            Padding = padding,
            Cursor = Cursors.Hand,
        };
        label.Click += (_, _) => click();
        label.Size = label.PreferredSize;
        return label;
    }
}
