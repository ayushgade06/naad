using System.Windows.Controls;
using System.Windows.Forms.Integration;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace Naad.Window;

/// <summary>When Esc closes a window. Pure: decided from the focused element alone.</summary>
internal static class EscapeKey
{
    /// <summary>
    /// Whether Esc closes the window with <paramref name="focused"/> having the keyboard focus. Not
    /// while typing: in a text box, a password box, an editable list, or the hosted settings form
    /// (whose own boxes WPF can't see), Esc belongs to what is being typed.
    /// </summary>
    public static bool Closes(object? focused) =>
        focused is not (TextBoxBase or PasswordBox or ComboBox { IsEditable: true } or WindowsFormsHost);
}
