using Naad.Native;

namespace Naad.Tray;

/// <summary>
/// An almost invisible window laid over the part of the overlay that takes clicks: the stop
/// button, or the whole pill while it offers an update. The overlay itself stays click-through,
/// so a click anywhere else still reaches the app below. Like the overlay it never activates, so
/// clicking it doesn't move the focus the paste depends on. Use on the UI thread.
/// </summary>
internal sealed class OverlayClickTarget : Form
{
    // Opacity 0 would let clicks through; the lowest step above it is invisible to the eye.
    private const double NearlyInvisible = 0.01;

    private Action? _clicked;

    public OverlayClickTarget()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = NearlyInvisible;
        Cursor = Cursors.Hand;
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

    /// <summary>
    /// Takes clicks over <paramref name="bounds"/> (screen pixels) and calls <paramref name="clicked"/>.
    /// </summary>
    public void ShowAt(Rectangle bounds, Action clicked)
    {
        _clicked = clicked;
        Bounds = bounds;
        if (!Visible)
        {
            Show();
        }
    }

    /// <summary>Stops taking clicks.</summary>
    public void Clear()
    {
        _clicked = null;
        Hide();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _clicked is { } clicked)
        {
            Clear();
            clicked();
        }
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
}
