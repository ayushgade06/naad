using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Insertion;

/// <summary>
/// Whether text can be typed while keys are held, and which held keys to lift first. Typing goes
/// on while the user holds the dictation shortcut, so a held Ctrl must not turn the typed letters
/// into shortcuts. Pure: decided from the keys down and the shortcut.
/// </summary>
internal static class HeldModifiers
{
    /// <summary>The modifiers a keyboard reports by side, which <see cref="TextInserter"/> reads.</summary>
    public static readonly Key[] All =
    [
        Key.LShiftKey, Key.RShiftKey, Key.LControlKey, Key.RControlKey,
        Key.LMenu, Key.RMenu, Key.LWin, Key.RWin,
    ];

    /// <summary>
    /// True when a held modifier must be let go before typing: one that isn't part of the
    /// shortcut, or an Alt or Windows key in it. Letting go of Alt alone opens the app's menu, and
    /// of Windows alone the Start menu, so those are never lifted for the user.
    /// </summary>
    /// <param name="down">The modifiers held now.</param>
    /// <param name="shortcuts">The dictation shortcuts, whose Ctrl and Shift keys may stay held.</param>
    public static bool BlockTyping(IReadOnlyCollection<Key> down, IReadOnlyList<Chord> shortcuts) =>
        down.Any(key => !shortcuts.Any(s => s.Contains(key)) || !Liftable(key));

    /// <summary>The shortcuts' Ctrl and Shift keys that are held, to release before typing.</summary>
    public static IReadOnlyList<Key> ToLift(IReadOnlyCollection<Key> down, IReadOnlyList<Chord> shortcuts) =>
        [.. down.Where(key => shortcuts.Any(s => s.Contains(key)) && Liftable(key))];

    private static bool Liftable(Key key) =>
        key is Key.LControlKey or Key.RControlKey or Key.LShiftKey or Key.RShiftKey;
}
