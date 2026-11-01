using Naad.Hotkey;
using Naad.Settings;

namespace Naad.Tray;

// The tray's shortcut submenus: hold to talk, the toggle and paste again, each with Off, its
// presets, its last custom chord and Customize.
internal sealed partial class TrayIcon
{
    /// <summary>
    /// Adds a submenu per shortcut role, filled when it opens. A choice that clashes with another
    /// role is greyed out, so every choice offered can be saved. Call once, on the UI thread.
    /// </summary>
    /// <param name="current">The settings in use.</param>
    /// <param name="save">Saves and applies new settings; returns what went wrong, or null.</param>
    /// <param name="customize">Opens the settings, to capture other keys.</param>
    public void AddShortcutMenus(Func<AppSettings> current, Func<AppSettings, string?> save, Action customize)
    {
        var menu = new ToolStripMenuItem("Shortcuts");
        foreach (var role in Shortcuts.Roles)
        {
            var item = new ToolStripMenuItem(Shortcuts.Name(role));

            // A placeholder, so the submenu shows its arrow before it is first filled.
            item.DropDownItems.Add(new ToolStripMenuItem("Off"));
            item.DropDownOpening += (_, _) => FillShortcutMenu(item, role, current(), save, customize);
            menu.DropDownItems.Add(item);
        }

        _menu.Items.Insert(_menu.Items.IndexOf(_toggleDictation) + 1, menu);
    }

    private void FillShortcutMenu(
        ToolStripMenuItem menu, ShortcutRole role, AppSettings s, Func<AppSettings, string?> save, Action customize)
    {
        menu.DropDownItems.Clear();
        var chosen = Shortcuts.Get(s, role);
        var choices = new List<Chord?> { null };
        choices.AddRange(Shortcuts.Presets(role));
        if (Shortcuts.Custom(s, role) is { } custom && !choices.Contains(custom))
        {
            choices.Add(custom);
        }

        if (chosen is not null && !choices.Contains(chosen))
        {
            choices.Add(chosen);
        }

        foreach (var chord in choices)
        {
            var (next, problem) = Shortcuts.Choose(s, role, chord);
            var label = chord is null ? "Off"
                : Shortcuts.Presets(role).Contains(chord) ? chord.Describe()
                : "Custom: " + chord.Describe();
            var item = new ToolStripMenuItem(label)
            {
                Checked = chord == chosen,
                Enabled = next is not null || chord == chosen,
                ToolTipText = problem,
            };
            item.Click += (_, _) =>
            {
                if (next is not null && chord != chosen && save(next) is { } error)
                {
                    Notify("Naad shortcuts", error, ToolTipIcon.Warning, clicked: null);
                }
            };
            menu.DropDownItems.Add(item);
        }

        menu.DropDownItems.Add(new ToolStripSeparator());
        menu.DropDownItems.Add(new ToolStripMenuItem("Customize...", null, (_, _) => customize()));
    }
}
