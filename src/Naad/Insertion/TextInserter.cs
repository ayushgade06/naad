using System.Runtime.InteropServices;
using Naad.Native;

namespace Naad.Insertion;

/// <summary>Puts text into the focused app through the clipboard and a synthetic Ctrl+V.</summary>
internal static class TextInserter
{
    // The user normally lets go within a few hundred ms of releasing the dictation key.
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private static readonly ushort[] Modifiers =
        [User32.VkShift, User32.VkControl, User32.VkMenu, User32.VkLWin, User32.VkRWin];

    /// <summary>Call on the UI thread: the WinForms clipboard requires STA.</summary>
    // ponytail: overwrites the user's clipboard; P14 snapshots and restores it.
    public static void CopyToClipboard(string text) =>
        Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 20);

    /// <summary>
    /// Waits until no modifier is physically held, so Ctrl+V doesn't become
    /// Ctrl+Shift+V or Ctrl+Alt+V. Returns false if the user is still holding one.
    /// </summary>
    public static async Task<bool> WaitForModifiersReleasedAsync()
    {
        var deadline = DateTime.UtcNow + ModifierReleaseTimeout;
        while (Modifiers.Any(IsDown))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval);
        }

        return true;
    }

    /// <summary>
    /// Sends Ctrl+V to the foreground window. Returns false if Windows rejected the input.
    /// Input blocked by UIPI (an elevated target window) still reports success; see P15.
    /// </summary>
    public static bool SendPaste()
    {
        User32.Input[] inputs =
        [
            Key(User32.VkControl, up: false),
            Key(User32.VkV, up: false),
            Key(User32.VkV, up: true),
            Key(User32.VkControl, up: true),
        ];
        var sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<User32.Input>());
        return sent == inputs.Length;
    }

    // The high bit of GetAsyncKeyState is set while the key is physically down.
    private static bool IsDown(ushort vk) => User32.GetAsyncKeyState(vk) < 0;

    private static User32.Input Key(ushort vk, bool up) => new()
    {
        Type = User32.InputKeyboard,
        Union = new User32.InputUnion
        {
            Keyboard = new User32.KeyboardInput { Vk = vk, Flags = up ? User32.KeyEventFKeyUp : 0 },
        },
    };
}
