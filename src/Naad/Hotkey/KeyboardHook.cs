using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Naad.Native;

namespace Naad.Hotkey;

/// <summary>
/// Watches the dictation shortcut through a low-level keyboard hook; the
/// <see cref="ChordMatcher"/> decides what the keys mean. Create it on the UI thread:
/// Windows calls the hook on the installing thread's message loop.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    /// <summary>
    /// Naad's own synthetic input carries this in dwExtraInfo, so the hook can tell it apart
    /// from other programs' input. The value is "NAAD" in ASCII.
    /// </summary>
    public const nuint OwnInputMarker = 0x4E41_4144;

    // Held in a field: Windows keeps only a raw pointer, so the GC must not collect the delegate.
    private readonly User32.HookProc _proc;
    private readonly nint _handle;
    private readonly SynchronizationContext _ui;
    private readonly ChordMatcher _matcher;

    public KeyboardHook(Chord shortcut)
    {
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook must be created on a thread with a message loop.");
        _matcher = new ChordMatcher([shortcut]);
        _proc = OnKey;
        _handle = User32.SetWindowsHookEx(
            User32.WhKeyboardLl,
            Marshal.GetFunctionPointerForDelegate(_proc),
            Marshal.GetHINSTANCE(typeof(KeyboardHook).Module),
            0);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public event Action? TriggerPressed;

    public event Action? TriggerReleased;

    /// <summary>
    /// Another key went down while the shortcut was held, e.g. Right Ctrl+C. No
    /// <see cref="TriggerReleased"/> follows.
    /// </summary>
    public event Action? TriggerCancelled;

    public void Dispose() => User32.UnhookWindowsHookEx(_handle);

    /// <summary>
    /// Whether the hook skips an input event. Naad's own Ctrl+V must never reach the matcher.
    /// Other programs' synthetic input counts like a physical key, so a key remapper can provide
    /// the shortcut (for example a Copilot key remapped to Right Ctrl), except input from a
    /// lower-integrity process such as a sandbox, which must not be able to start a recording.
    /// </summary>
    public static bool ShouldIgnore(uint flags, nuint extraInfo) =>
        (flags & User32.LlkhfLowerIlInjected) != 0
        || ((flags & User32.LlkhfInjected) != 0 && extraInfo == OwnInputMarker);

    // The matcher runs here rather than after the callback, because whether to swallow the
    // key must be known before the callback returns. It is quick and does no I/O; the event
    // handlers, which open the microphone and so on, are queued.
    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<User32.KbdLlHookStruct>(lParam);
            if (!ShouldIgnore(info.Flags, info.ExtraInfo)
                && OnKeyEvent(info.VkCode, wParam is User32.WmKeyDown or User32.WmSysKeyDown))
            {
                // A non-zero result without calling the next hook keeps the key from every app.
                return 1;
            }
        }

        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    // Returns whether to swallow the key event.
    private bool OnKeyEvent(uint vk, bool down)
    {
        var outcome = _matcher.OnKey((Keys)vk, down);
        foreach (var e in outcome.Events)
        {
            Raise(e.Kind switch
            {
                ChordEventKind.Pressed => TriggerPressed,
                ChordEventKind.Released => TriggerReleased,
                ChordEventKind.Cancelled => TriggerCancelled,
                _ => throw new UnreachableException(),
            });
        }

        return outcome.Swallow;
    }

    // Windows silently removes hooks whose callback is slow, so handlers run after the
    // callback has returned.
    private void Raise(Action? handler)
    {
        if (handler is not null)
        {
            _ui.Post(_ => handler(), null);
        }
    }
}
