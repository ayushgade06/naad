using System.Drawing.Drawing2D;
using Naad.Dictation;

namespace Naad.Tray;

// How the overlay draws itself: the pill, the bars or a label, live words, the hands-free stop
// button and the editing look. The state it draws is kept in RecordingOverlay.cs.
internal sealed partial class RecordingOverlay
{
    // The compact style's bars are this much smaller, to fit its shorter capsule.
    private const float CompactBarScale = 0.67f;

    private static readonly Color StopColor = Color.FromArgb(0xE5, 0x48, 0x4D);
    private static readonly Color EditColor = Color.FromArgb(0xF5, 0xB7, 0x3B);

    private void Draw(Graphics graphics)
    {
        using (var pill = Capsule(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1)))
        using (var fill = new SolidBrush(Background))
        using (var edge = new Pen(Border, 1))
        {
            graphics.FillPath(fill, pill);
            graphics.DrawPath(edge, pill);
        }

        // Live words on top; the bars or the label in the band below, where they always are.
        var bandHeight = Math.Min(Height, Scale(LogicalHeight));
        var band = new RectangleF(0, Height - bandHeight, Width, bandHeight);
        if (_liveText.Length > 0)
        {
            DrawLiveText(graphics);
        }

        // The microphone can take a moment to open; until then there are no levels to show.
        if (_state == DictationState.Recording && !_microphoneOpen)
        {
            DrawLabel(graphics, "Starting", band);
            return;
        }

        if (_state != DictationState.Recording || _notice.Enabled)
        {
            DrawLabel(graphics, _label, band);
            return;
        }

        // The stop button takes the right end; the bars centre in what is left.
        var stop = _handsFree ? OverlayLayout.StopButton(Bounds, DeviceDpi / 96.0) : Rectangle.Empty;
        var bars = band with { Width = _handsFree ? stop.Left - Left - (stop.Width / 4f) : band.Width };
        if (_editing)
        {
            DrawPencil(graphics, band);
        }

        DrawBars(graphics, bars);
        if (_handsFree)
        {
            DrawStop(graphics, stop with { X = stop.X - Left, Y = stop.Y - Top });
        }

        if (_showTimer)
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_recordingStarted);
            using var text = new SolidBrush(TextColor);
            using var small = new Font(Font.FontFamily, Font.Size * 0.85f);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(
                $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}",
                small,
                text,
                bars with { Width = bars.Width - Scale(LogicalBarGap * 4) },
                format);
        }
    }

    private void DrawLabel(Graphics graphics, string label, RectangleF band)
    {
        using var text = new SolidBrush(TextColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(label, Font, text, band, format);
    }

    // The newest words that fit on one line, cut at a word with an ellipsis in front.
    private void DrawLiveText(Graphics graphics)
    {
        var line = new RectangleF(
            Scale(LogicalTextPadding),
            Scale(LogicalLiveTextTop),
            Width - Scale(LogicalTextPadding * 2),
            Scale(LogicalLiveTextHeight));
        var words = _liveText;
        var cut = false;
        while (graphics.MeasureString((cut ? Ellipsis : "") + words, Font).Width > line.Width
            && words.IndexOf(' ', StringComparison.Ordinal) is var space and > 0)
        {
            words = words[(space + 1)..];
            cut = true;
        }

        using var brush = new SolidBrush(BarColor);
        using var format = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };
        graphics.DrawString((cut ? Ellipsis : "") + words, Font, brush, line, format);
    }

    // Each bar is a capsule never shorter than it is wide, so silence shows a row of dots; the
    // compact style draws them smaller. An edit's bars are amber, so it is plain what will happen.
    private void DrawBars(Graphics graphics, RectangleF band)
    {
        var compact = band.Height < Scale(LogicalHeight);
        float barWidth = compact ? Scale(LogicalBarWidth) * CompactBarScale : Scale(LogicalBarWidth);
        float gap = compact ? Scale(LogicalBarGap) * CompactBarScale : Scale(LogicalBarGap);
        float maxHeight = compact ? band.Height - (2 * gap) : Scale(LogicalMaxBarHeight);
        float step = barWidth + gap;
        var left = band.Left + ((band.Width - ((BarCount * step) - gap)) / 2f);
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(_recordingStarted).TotalSeconds;
        using var brush = new SolidBrush(_editing ? EditColor : BarColor);
        for (var i = 0; i < BarCount; i++)
        {
            // Halfway to the new level at each buffer, so the bars glide instead of jumping.
            _shown[i] += ((float)_bars.Breathing(i, seconds) - _shown[i]) * 0.5f;
            var height = Math.Max(barWidth, _shown[i] * maxHeight);
            using var bar = Capsule(
                new RectangleF(left + (i * step), band.Top + ((band.Height - height) / 2f), barWidth, height));
            graphics.FillPath(brush, bar);
        }
    }

    // A red circle with a white square: the usual stop sign, where the click target lies.
    private static void DrawStop(Graphics graphics, Rectangle button)
    {
        using var red = new SolidBrush(StopColor);
        using var white = new SolidBrush(Color.White);
        graphics.FillEllipse(red, button);
        var side = button.Width * 0.36f;
        graphics.FillRectangle(
            white,
            button.Left + ((button.Width - side) / 2f),
            button.Top + ((button.Height - side) / 2f),
            side,
            side);
    }

    // A pencil glyph at the left end, so an edit is told apart from a dictation at a glance.
    private void DrawPencil(Graphics graphics, RectangleF band)
    {
        using var brush = new SolidBrush(EditColor);
        using var font = new Font("Segoe UI Symbol", Font.Size);
        using var format = new StringFormat { LineAlignment = StringAlignment.Center };
        graphics.DrawString("\u270E", font, brush, band with { X = band.X + Scale(LogicalBarGap * 2) }, format);
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
}
