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

    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<User32.KbdLlHookStruct>(lParam);
            if ((info.Flags & User32.LlkhfInjected) == 0)
            {
                OnPhysicalKey(info.VkCode, wParam is User32.WmKeyDown or User32.WmSysKeyDown);
            }
        }

        // Always pass the key on: the shortcut's keys keep working in every app.
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private void OnPhysicalKey(uint vk, bool down)
    {
        foreach (var e in _matcher.OnKey((Keys)vk, down).Events)
        {
            Raise(e.Kind switch
            {
                ChordEventKind.Pressed => TriggerPressed,
                ChordEventKind.Released => TriggerReleased,
                ChordEventKind.Cancelled => TriggerCancelled,
                _ => throw new UnreachableException(),
            });
        }
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
