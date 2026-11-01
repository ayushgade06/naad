using System.Runtime.InteropServices;
using Naad.Hotkey;
using Naad.Native;

namespace Naad.Insertion;

internal enum TypingResult
{
    /// <summary>Windows accepted every key.</summary>
    Typed,

    /// <summary>Windows accepted none, so nothing was typed.</summary>
    Rejected,

    /// <summary>Windows accepted only some keys; part of the text may have been typed.</summary>
    Partial,
}

/// <summary>
/// Types text as Unicode key events (KEYEVENTF_UNICODE), the fallback when the clipboard can't
/// be used. Works for any script and for emoji, and never touches the clipboard.
/// </summary>
internal static class UnicodeTyping
{
    /// <summary>
    /// Sends the text to the focused window in one SendInput call, after releasing
    /// <paramref name="lift"/>; see <see cref="Build"/>.
    /// </summary>
    public static TypingResult Type(string text, IReadOnlyList<System.Windows.Forms.Keys>? lift = null)
    {
        var inputs = Build(text, lift);
        if (inputs.Length == 0)
        {
            return TypingResult.Typed;
        }

        var sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<User32.Input>());
        return sent == inputs.Length ? TypingResult.Typed
            : sent == 0 ? TypingResult.Rejected
            : TypingResult.Partial;
    }

    /// <summary>
    /// A key down and up per UTF-16 code unit. A character outside the Basic Multilingual Plane
    /// (an emoji) is two code units, sent back to back, which apps combine. Line breaks become
    /// Enter, since a Unicode "\n" isn't a key press most apps act on; "\r\n" is one Enter.
    /// </summary>
    /// <param name="text">What to type.</param>
    /// <param name="lift">Held keys to release first, in the same call, so a held Ctrl doesn't make
    /// the letters shortcuts. They aren't pressed again: the user's own release comes later.</param>
    public static User32.Input[] Build(string text, IReadOnlyList<System.Windows.Forms.Keys>? lift = null)
    {
        var inputs = new List<User32.Input>((text.Length * 2) + (lift?.Count ?? 0));
        foreach (var key in lift ?? [])
        {
            inputs.Add(Key((ushort)key, '\0', up: true));
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue;
            }

            if (c is '\r' or '\n')
            {
                inputs.Add(Key(User32.VkReturn, '\0', up: false));
                inputs.Add(Key(User32.VkReturn, '\0', up: true));
            }
            else
            {
                inputs.Add(Key(0, c, up: false));
                inputs.Add(Key(0, c, up: true));
            }
        }

        return [.. inputs];
    }

    private static User32.Input Key(ushort vk, char unit, bool up) => new()
    {
        Type = User32.InputKeyboard,
        Union = new User32.InputUnion
        {
            Keyboard = new User32.KeyboardInput
            {
                Vk = vk,
                Scan = unit,
                Flags = (vk == 0 ? User32.KeyEventFUnicode : 0) | (up ? User32.KeyEventFKeyUp : 0)

                    // Without it, Windows takes an injected Right Ctrl for the left one, and the right
                    // one would stay held.
                    // ponytail: Right Shift isn't an extended key; if a lifted Right Shift stays held
                    // (M-FX1), send its scan code (0x36) with KEYEVENTF_SCANCODE.
                    | (vk is (ushort)System.Windows.Forms.Keys.RControlKey ? User32.KeyEventFExtendedKey : 0),

                // Marks the keys as Naad's own, so the keyboard hook skips them.
                ExtraInfo = KeyboardHook.OwnInputMarker,
            },
        },
    };
}
