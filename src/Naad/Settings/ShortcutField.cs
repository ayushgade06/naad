using Naad.Hotkey;

namespace Naad.Settings;

/// <summary>
/// A drop-down for one shortcut role: Off, its presets, its last custom chord, and "Other
/// keys..." to capture a new one. Create and use it on the UI thread.
/// </summary>
internal sealed class ShortcutField
{
    private const string CaptureItem = "Other keys...";
    private const string CapturePrompt = "Press the keys, then let go. Esc cancels.";

    private readonly ShortcutRole _role;
    private readonly List<Chord?> _choices = [];
    private bool _filling;

    /// <param name="role">The role it sets.</param>
    /// <param name="current">The role's shortcut now, or null when off.</param>
    /// <param name="custom">The role's last custom chord, or null.</param>
    /// <param name="width">The drop-down's width.</param>
    public ShortcutField(ShortcutRole role, Chord? current, Chord? custom, int width)
    {
        _role = role;
        Chosen = current;
        Custom = custom;
        Box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        Box.SelectedIndexChanged += (_, _) => OnSelected();
        Fill();
    }

    /// <summary>"Other keys..." was chosen: the window should start a capture.</summary>
    public event Action<ShortcutField>? CaptureRequested;

    public ComboBox Box { get; }

    /// <summary>The chosen shortcut, or null for Off.</summary>
    public Chord? Chosen { get; private set; }

    /// <summary>The last chord captured that isn't a preset.</summary>
    public Chord? Custom { get; private set; }

    /// <summary>Shows that the next keys pressed are being captured.</summary>
    public void ShowCapturing()
    {
        _filling = true;
        Box.Items.Clear();
        Box.Items.Add(CapturePrompt);
        Box.SelectedIndex = 0;
        _filling = false;
    }

    /// <summary>A capture ended: <paramref name="chord"/> is chosen, or null to keep the last choice.</summary>
    public void EndCapture(Chord? chord)
    {
        if (chord is not null)
        {
            Chosen = chord;
            if (!Shortcuts.Presets(_role).Contains(chord))
            {
                Custom = chord;
            }
        }

        Fill();
    }

    private void Fill()
    {
        _filling = true;
        Box.Items.Clear();
        _choices.Clear();
        Add("Off", null);
        foreach (var preset in Shortcuts.Presets(_role))
        {
            Add(preset.Describe(), preset);
        }

        foreach (var other in new[] { Custom, Chosen })
        {
            if (other is not null && !_choices.Contains(other))
            {
                Add("Custom: " + other.Describe(), other);
            }
        }

        Box.Items.Add(CaptureItem);
        Box.SelectedIndex = _choices.IndexOf(Chosen);
        _filling = false;
    }

    private void Add(string text, Chord? chord)
    {
        Box.Items.Add(text);
        _choices.Add(chord);
    }

    private void OnSelected()
    {
        if (_filling || Box.SelectedIndex < 0)
        {
            return;
        }

        if (Box.SelectedIndex < _choices.Count)
        {
            Chosen = _choices[Box.SelectedIndex];
            return;
        }

        CaptureRequested?.Invoke(this);
    }
}
