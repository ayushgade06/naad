using Naad.Window;

namespace Naad.Settings;

/// <summary>The few layout pieces every settings page builds with, and its dark colours.</summary>
internal static class FormLayout
{
    /// <summary>Wide enough for a URL or a model id; the window sizes itself around it.</summary>
    public const int FieldWidth = 300;

    /// <summary>Wrapping text, at most a field and a button wide.</summary>
    public static Label Note(string text = "") =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(FieldWidth + 120, 0) };

    /// <summary>Controls side by side, as one cell.</summary>
    public static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        return flow;
    }

    /// <summary>A label and its control, as one row of a two-column table.</summary>
    public static void Row(TableLayoutPanel table, string label, Control control)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        table.Controls.Add(control);
    }

    /// <summary>
    /// Colours <paramref name="control"/> and everything in it with <paramref name="palette"/>.
    /// WinForms has no dark mode of its own, so it is done control by control.
    /// </summary>
    // ponytail: tab headers, scroll bars and drop-down lists keep Windows' light look.
    public static void ApplyPalette(Control control, Palette palette)
    {
        control.BackColor = Colour(control is TextBoxBase or ComboBox or NumericUpDown
            ? palette.Surface
            : palette.Background);
        control.ForeColor = Colour(palette.Text);
        if (control is TabPage page)
        {
            page.UseVisualStyleBackColor = false;
        }

        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Colour(palette.Border);
        }

        foreach (Control child in control.Controls)
        {
            ApplyPalette(child, palette);
        }
    }

    private static Color Colour(uint argb) => Color.FromArgb(unchecked((int)argb));
}
