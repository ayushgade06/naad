using Naad.Context;
using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Settings;

/// <summary>What a shortcut does. Each role has its own shortcut, or none.</summary>
internal enum ShortcutRole
{
    /// <summary>Held while speaking; letting go pastes.</summary>
    Hold,

    /// <summary>Pressed to start and again to stop: hands-free.</summary>
    Toggle,

    /// <summary>Pastes the last dictation again.</summary>
    PasteAgain,
}

/// <summary>
/// The shortcut roles: their presets, the chord each one has, and the rules that keep them apart.
/// Pure: decided from the settings alone.
/// </summary>
internal static class Shortcuts
{
    public static readonly ShortcutRole[] Roles = [ShortcutRole.Hold, ShortcutRole.Toggle, ShortcutRole.PasteAgain];

    // A few chords that are free on most keyboards and in most apps. Right Alt is AltGr on many
    // layouts, so it is offered but isn't the default.
    private static readonly Chord[] DictationPresets =
        [Chord.RightCtrl, Parse("RightAlt"), Parse("LeftCtrl+LeftWin"), Parse("F9"), Parse("F10")];

    private static readonly Chord[] PastePresets = [Parse("RightAlt+V"), Parse("RightCtrl+RightShift+V"), Parse("F10")];

    // Typing keys that would stop working in every app if a shortcut were made of them alone.
    private static readonly Key[] TypingKeys = [Key.Space, Key.Enter, Key.Tab, Key.Back, Key.Escape];

    /// <summary>The role's name for people.</summary>
    public static string Name(ShortcutRole role) => role switch
    {
        ShortcutRole.Hold => "Hold to talk",
        ShortcutRole.Toggle => "Toggle (hands-free)",
        _ => "Paste again",
    };

    /// <summary>The chords offered for <paramref name="role"/>, besides Disabled and a custom one.</summary>
    public static IReadOnlyList<Chord> Presets(ShortcutRole role) =>
        role == ShortcutRole.PasteAgain ? PastePresets : DictationPresets;

    /// <summary>
    /// The shortcut to tell people about, and whether it is pressed rather than held: hold to talk
    /// when it is on, the toggle otherwise.
    /// </summary>
    public static (Chord Shortcut, bool HandsFree) Primary(AppSettings s) =>
        s.HoldShortcut is { } hold ? (hold, false) : (s.ToggleShortcut ?? Chord.RightCtrl, true);

    /// <summary>The role's shortcut; null when it is disabled.</summary>
    public static Chord? Get(AppSettings s, ShortcutRole role) => role switch
    {
        ShortcutRole.Hold => s.HoldShortcut,
        ShortcutRole.Toggle => s.ToggleShortcut,
        _ => s.PasteAgainShortcut,
    };

    /// <summary>The last chord captured for the role that isn't a preset, or null.</summary>
    public static Chord? Custom(AppSettings s, ShortcutRole role) => role switch
    {
        ShortcutRole.Hold => s.CustomHoldShortcut,
        ShortcutRole.Toggle => s.CustomToggleShortcut,
        _ => s.CustomPasteAgainShortcut,
    };

    /// <summary>
    /// <paramref name="s"/> with <paramref name="chord"/> (null to disable) for
    /// <paramref name="role"/>, remembering it as the role's custom chord when it isn't a preset;
    /// or the reason it can't be, from <see cref="Problem"/>.
    /// </summary>
    public static (AppSettings? Settings, string? Error) Choose(AppSettings s, ShortcutRole role, Chord? chord)
    {
        var custom = chord is not null && !Presets(role).Contains(chord);
        var next = role switch
        {
            ShortcutRole.Hold => s with
            {
                HoldShortcut = chord,
                CustomHoldShortcut = custom ? chord : s.CustomHoldShortcut,
            },
            ShortcutRole.Toggle => s with
            {
                ToggleShortcut = chord,
                CustomToggleShortcut = custom ? chord : s.CustomToggleShortcut,
            },
            _ => s with
            {
                PasteAgainShortcut = chord,
                CustomPasteAgainShortcut = custom ? chord : s.CustomPasteAgainShortcut,
            },
        };
        return Problem(next) is { } error ? (null, error) : (next, null);
    }

    /// <summary>
    /// Why the shortcuts can't work together, naming the roles involved, or null when they can: a
    /// way to dictate must stay on, no two roles share a chord, no shortcut is a typing key on its
    /// own, and the Edit Mode key isn't part of a dictation shortcut.
    /// </summary>
    public static string? Problem(AppSettings s)
    {
        if (s.HoldShortcut is null && s.ToggleShortcut is null)
        {
            return "Hold to talk or Toggle must have a shortcut: one way to dictate stays on.";
        }

        var used = Roles.Where(r => Get(s, r) is not null).Select(r => (Role: r, Chord: Get(s, r)!)).ToList();
        foreach (var (role, chord) in used)
        {
            if (chord.Keys.Count == 1 && (TypingKeys.Contains(chord.Keys[0]) || IsCharacterKey(chord.Keys[0])))
            {
                return $"{Name(role)} can't be {chord.Describe()} alone: that key would stop typing in every app.";
            }

            // Pressed and done, paste again would fire each time the modifier is used, as in Ctrl+C.
            if (role == ShortcutRole.PasteAgain && chord.Keys.All(Chord.IsModifier))
            {
                return "Paste again needs a key besides Ctrl, Shift, Alt or Windows, such as Right Alt+V.";
            }
        }

        for (var i = 0; i < used.Count; i++)
        {
            for (var j = i + 1; j < used.Count; j++)
            {
                if (used[i].Chord == used[j].Chord)
                {
                    return $"{Name(used[i].Role)} and {Name(used[j].Role)} both use {used[i].Chord.Describe()}. " +
                        "Choose another shortcut for one of them.";
                }
            }
        }

        if (s.EditMode == EditMode.Manual)
        {
            var sides = EditKeySides(s.EditModeKey);
            foreach (var (role, chord) in used.Where(u => u.Role != ShortcutRole.PasteAgain))
            {
                if (chord.Keys.Any(sides.Contains))
                {
                    return $"The Edit Mode key ({s.EditModeKey}) is part of {Name(role)}'s shortcut, so every " +
                        "dictation would be an edit. Choose another Edit Mode key or shortcut.";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// <paramref name="s"/> with only what makes <see cref="Problem"/> fail undone, the rest kept: a
    /// typing key alone or a modifier-only paste again is turned off, a clash drops paste again
    /// before the toggle, an Edit Mode key inside a dictation shortcut makes Edit Mode Automatic,
    /// and with no dictation shortcut left, hold to talk gets Right Ctrl.
    /// </summary>
    public static AppSettings Repair(AppSettings s)
    {
        bool Unusable(Chord? chord, ShortcutRole role) => chord is not null && (
            (chord.Keys.Count == 1 && (TypingKeys.Contains(chord.Keys[0]) || IsCharacterKey(chord.Keys[0])))
            || (role == ShortcutRole.PasteAgain && chord.Keys.All(Chord.IsModifier)));

        s = s with
        {
            HoldShortcut = Unusable(s.HoldShortcut, ShortcutRole.Hold) ? null : s.HoldShortcut,
            ToggleShortcut = Unusable(s.ToggleShortcut, ShortcutRole.Toggle) ? null : s.ToggleShortcut,
            PasteAgainShortcut = Unusable(s.PasteAgainShortcut, ShortcutRole.PasteAgain) ? null : s.PasteAgainShortcut,
        };
        if (s.HoldShortcut is null && s.ToggleShortcut is null)
        {
            s = s with { HoldShortcut = Chord.RightCtrl };
        }

        if (s.PasteAgainShortcut is { } paste && (paste == s.HoldShortcut || paste == s.ToggleShortcut))
        {
            s = s with { PasteAgainShortcut = null };
        }

        if (s.ToggleShortcut is not null && s.ToggleShortcut == s.HoldShortcut)
        {
            s = s with { ToggleShortcut = null };
        }

        var sides = EditKeySides(s.EditModeKey);
        if (s.EditMode == EditMode.Manual
            && new[] { s.HoldShortcut, s.ToggleShortcut }.Any(c => c is not null && c.Keys.Any(sides.Contains)))
        {
            s = s with { EditMode = EditMode.Automatic };
        }

        return s;
    }

    /// <summary>The left and right keys of the Edit Mode key.</summary>
    public static Key[] EditKeySides(EditModeKey key) => key switch
    {
        EditModeKey.Ctrl => [Key.LControlKey, Key.RControlKey],
        _ => [Key.LShiftKey, Key.RShiftKey],
    };

    // Letters, digits and punctuation: the keys that type a character.
    private static bool IsCharacterKey(Key key) =>
        key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.Divide)
            or (>= Key.OemSemicolon and <= Key.OemBackslash);

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException("Not a chord.", nameof(text));
}
