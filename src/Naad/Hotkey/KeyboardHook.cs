using System.ComponentModel;
using System.Runtime.InteropServices;
using Naad.Native;

namespace Naad.Hotkey;

/// <summary>
/// Watches the dictation key through a low-level keyboard hook. Create it on the UI
/// thread: Windows calls the hook on the installing thread's message loop.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    // ponytail: hard-coded Right Ctrl; P7/P8 replace this with configurable chords.
    private const uint TriggerKey = User32.VkRControl;

    // Held in a field: Windows keeps only a raw pointer, so the GC must not collect the delegate.
    private readonly User32.HookProc _proc;
    private readonly nint _handle;
    private readonly SynchronizationContext _ui;
    private bool _triggerDown;

    public KeyboardHook()
    {
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook must be created on a thread with a message loop.");
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

    /// <summary>Another key went down while the trigger was held, e.g. Right Ctrl+C.</summary>
    public event Action? OtherKeyPressed;

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

        // Always pass the key on: Right Ctrl keeps working as Ctrl in every app.
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private void OnPhysicalKey(uint vk, bool down)
    {
        if (vk != TriggerKey)
        {
            if (down && _triggerDown)
            {
                Raise(OtherKeyPressed);
            }

            return;
        }

        // Holding a key repeats key-downs; only the first one is a press.
        if (down && !_triggerDown)
        {
            _triggerDown = true;
            Raise(TriggerPressed);
        }
        else if (!down && _triggerDown)
        {
            _triggerDown = false;
            Raise(TriggerReleased);
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
