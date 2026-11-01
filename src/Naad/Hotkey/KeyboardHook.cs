using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Naad.Native;

namespace Naad.Hotkey;

internal enum CancelCause
{
    /// <summary>Another key went down, as in Right Ctrl+C.</summary>
    OtherKey,

    /// <summary>A mouse button or the wheel was used, as in Ctrl+click or Ctrl+scroll.</summary>
    Mouse,
}

/// <summary>
/// Watches the shortcuts through a low-level keyboard hook, and the mouse while one is held; the
/// <see cref="ChordMatcher"/> decides what the input means and <see cref="ShortcutRouter"/> which
/// role it is for. Create and
/// dispose it on the UI thread: Windows calls the hooks on the installing thread's message loop.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    /// <summary>
    /// Naad's own synthetic input carries this in dwExtraInfo, so the hook can tell it apart
    /// from other programs' input. The value is "NAAD" in ASCII.
    /// </summary>
    public const nuint OwnInputMarker = 0x4E41_4144;

    // Lost key-ups are found within this long. A dictation whose key-up was lost then ends
    // about two seconds late instead of running to its five-minute limit, and the check costs
    // nothing while no key is held.
    private const int HealthCheckMilliseconds = 2000;

    // Held in fields: Windows keeps only raw pointers, so the GC must not collect the delegates.
    private readonly User32.HookProc _proc;
    private readonly User32.HookProc _mouseProc;
    private readonly System.Windows.Forms.Timer _healthCheck = new() { Interval = HealthCheckMilliseconds };
    private nint _handle;
    private readonly SynchronizationContext _ui;
    private readonly ChordMatcher _matcher;
    private nint _mouseHandle;
    private bool _disposed;

    private Chord? _hold;
    private Chord? _toggle;
    private Chord? _pasteAgain;

    /// <param name="hold">Hold to talk's shortcut, or null for none.</param>
    /// <param name="toggle">The hands-free toggle's shortcut, or null for none.</param>
    /// <param name="pasteAgain">Paste again's shortcut, or null for none.</param>
    public KeyboardHook(Chord? hold, Chord? toggle, Chord? pasteAgain)
    {
        _ui = SynchronizationContext.Current
            ?? throw new InvalidOperationException("KeyboardHook must be created on a thread with a message loop.");
        _matcher = new ChordMatcher([]);
        SetShortcuts(hold, toggle, pasteAgain);
        _proc = OnKey;
        _mouseProc = OnMouse;
        _handle = Install(User32.WhKeyboardLl, _proc);
        if (_handle == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _healthCheck.Tick += (_, _) => Resync();
        _healthCheck.Start();

        // Windows can drop a low-level hook across sleep or a locked session without telling the
        // program, so the hook is installed afresh whenever the user comes back. SystemEvents
        // raises these on its own thread.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    /// <summary>The hold-to-talk shortcut went down.</summary>
    public event Action? TriggerPressed;

    /// <summary>The hold-to-talk shortcut was let go.</summary>
    public event Action? TriggerReleased;

    /// <summary>
    /// Another key or a mouse button was pressed while the hold-to-talk shortcut was held, e.g.
    /// Right Ctrl+C. No <see cref="TriggerReleased"/> follows.
    /// </summary>
    public event Action<CancelCause>? TriggerCancelled;

    /// <summary>The toggle shortcut went down: start, or stop, a hands-free dictation.</summary>
    public event Action? TogglePressed;

    /// <summary>Another key joined the toggle shortcut, as in a longer shortcut of another app.</summary>
    public event Action? ToggleCancelled;

    /// <summary>Esc was pressed while <see cref="EscapeArmed"/>; it was kept from the app.</summary>
    public event Action? EscapePressed;

    /// <summary>
    /// Space, or the keys that make the toggle shortcut, joined the held hold-to-talk shortcut:
    /// keep recording after it is let go.
    /// </summary>
    public event Action? Latched;

    /// <summary>The paste-again shortcut was pressed.</summary>
    public event Action? PasteAgainPressed;

    /// <summary>Whether Esc cancels a dictation; set while one runs. Call on the UI thread.</summary>
    public bool EscapeArmed
    {
        get => _matcher.EscapeArmed;
        set => _matcher.EscapeArmed = value;
    }

    /// <summary>Whether Space latches the held shortcut; set while a hold records. Call on the UI thread.</summary>
    public bool LatchArmed
    {
        get => _matcher.LatchArmed;
        set => _matcher.LatchArmed = value;
    }

    /// <summary>A capture started by <see cref="BeginCapture"/> captured, rejected or cancelled.</summary>
    public event Action<CaptureStep>? CaptureEnded;

    /// <summary>
    /// Takes the next keys pressed as a new shortcut and reports it through
    /// <see cref="CaptureEnded"/>. Until then every key pressed is swallowed and no dictation can
    /// start. Call on the UI thread while no dictation runs, and call <see cref="CancelCapture"/>
    /// as soon as the window asking for it closes or loses focus, since keys typed in other apps
    /// are swallowed too.
    /// </summary>
    public void BeginCapture()
    {
        _matcher.BeginCapture();
        SyncMouseHook();
    }

    /// <summary>Ends a capture early, without an event; harmless when none runs. Call on the UI thread.</summary>
    public void CancelCapture() => _matcher.CancelCapture();

    /// <summary>
    /// Watches for these shortcuts from now on (null for a role that is off), without reinstalling
    /// the hook. Settings keep them apart (<see cref="Naad.Settings.Shortcuts.Problem"/>). Call on the UI
    /// thread while no dictation runs: a shortcut held right now ends without an event.
    /// </summary>
    public void SetShortcuts(Chord? hold, Chord? toggle, Chord? pasteAgain)
    {
        (_hold, _toggle, _pasteAgain) = (hold, toggle, pasteAgain);
        _matcher.SetChords([.. new[] { hold, toggle, pasteAgain }.OfType<Chord>().Distinct()]);
        SyncMouseHook();
    }

    public void Dispose()
    {
        _disposed = true;

        // SystemEvents are static: an unremoved handler would outlive the hook.
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _healthCheck.Dispose();
        User32.UnhookWindowsHookEx(_handle);
        RemoveMouseHook();
    }

    /// <summary>
    /// Whether the hook skips an input event. Naad's own Ctrl+V must never reach the matcher.
    /// Other programs' synthetic input counts like a physical key, so a key remapper can provide
    /// the shortcut (for example a Copilot key remapped to Right Ctrl), except input from a
    /// lower-integrity process such as a sandbox, which must not be able to start a recording.
    /// </summary>
    public static bool ShouldIgnore(uint flags, nuint extraInfo) =>
        (flags & User32.LlkhfLowerIlInjected) != 0
        || ((flags & User32.LlkhfInjected) != 0 && extraInfo == OwnInputMarker);

    /// <summary>Mouse messages that cancel a held shortcut: button presses and wheel turns.</summary>
    public static bool IsButtonOrWheel(nint message) => message is User32.WmLButtonDown or User32.WmRButtonDown
        or User32.WmMButtonDown or User32.WmXButtonDown or User32.WmMouseWheel or User32.WmMouseHWheel;

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
        Raise(outcome.Events, CancelCause.OtherKey);
        if (outcome.Capture is { Status: not CaptureStatus.Capturing } step && CaptureEnded is { } ended)
        {
            _ui.Post(_ => ended(step), null);
        }

        return outcome.Swallow;
    }

    // Mouse input is never swallowed: Ctrl+click and Ctrl+scroll keep working in the app.
    private nint OnMouse(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsButtonOrWheel(wParam))
        {
            var info = Marshal.PtrToStructure<User32.MsLlHookStruct>(lParam);
            if ((info.Flags & User32.LlmhfLowerIlInjected) == 0)
            {
                Raise(_matcher.OnMouseButton(), CancelCause.Mouse);
            }
        }

        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    /// <summary>
    /// Replaces the keyboard hook with a new one and forgets keys whose key-up was missed. Keeps
    /// the old hook if a new one can't be installed. Call on the UI thread.
    /// </summary>
    // ponytail: reinstalls on resume and unlock only; Windows can also drop a hook whose callback
    // timed out, with no notice. A periodic reinstall would cover that if users report a dead shortcut.
    public void Reinstall()
    {
        if (_disposed)
        {
            return;
        }

        var replacement = Install(User32.WhKeyboardLl, _proc);
        if (replacement == 0)
        {
            Log.Error(LogEvent.HookReinstallFailed, ("win32_error", Marshal.GetLastPInvokeError()));
            return;
        }

        // The new hook goes in before the old one comes out, so no key goes unwatched. Hooks are
        // only called while this thread pumps messages, which it doesn't do in between, so no key
        // is seen twice either.
        User32.UnhookWindowsHookEx(_handle);
        _handle = replacement;
        Log.Info(LogEvent.HookReinstalled);
        Resync();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _ui.Post(_ => Reinstall(), null);
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
        {
            _ui.Post(_ => Reinstall(), null);
        }
    }

    private void Resync()
    {
        if (_disposed)
        {
            return;
        }

        // The high bit of GetAsyncKeyState is set while the key is physically down.
        var events = _matcher.Resync(key => User32.GetAsyncKeyState((int)key) < 0);
        if (events.Count > 0)
        {
            Log.Info(LogEvent.ShortcutKeysResynced, ("events", events.Count));
        }

        Raise(events, CancelCause.OtherKey);
    }

    // Windows silently removes hooks whose callback is slow, so handlers run after the
    // callback has returned.
    private void Raise(IReadOnlyList<ChordEvent> events, CancelCause cause)
    {
        if (events.Count == 0)
        {
            return;
        }

        foreach (var action in ShortcutRouter.Route(events, _hold, _toggle, _pasteAgain))
        {
            Action? handler = action switch
            {
                ShortcutAction.HoldPressed => TriggerPressed,
                ShortcutAction.HoldReleased => TriggerReleased,
                ShortcutAction.HoldCancelled => TriggerCancelled is { } cancelled ? () => cancelled(cause) : null,
                ShortcutAction.TogglePressed => TogglePressed,
                ShortcutAction.ToggleCancelled => ToggleCancelled,
                ShortcutAction.PasteAgainPressed => PasteAgainPressed,
                ShortcutAction.Escape => EscapePressed,
                ShortcutAction.Latched => Latched,
                _ => throw new UnreachableException(),
            };
            if (handler is not null)
            {
                _ui.Post(_ => handler(), null);
            }
        }

        _ui.Post(_ => SyncMouseHook(), null);
    }

    // The mouse hook is installed only while the shortcut is held. It sees every mouse move and
    // runs on this thread, so leaving it installed would make the pointer stutter whenever the
    // UI thread is busy.
    private void SyncMouseHook()
    {
        if (_disposed)
        {
            return;
        }

        if (!_matcher.IsActive)
        {
            RemoveMouseHook();
        }
        else if (_mouseHandle == 0)
        {
            _mouseHandle = Install(User32.WhMouseLl, _mouseProc);
            if (_mouseHandle == 0)
            {
                // Dictation still works; only Ctrl+click and Ctrl+scroll can't cancel it.
                Log.Warn(LogEvent.MouseHookUnavailable, ("win32_error", Marshal.GetLastPInvokeError()));
            }
        }
    }

    private void RemoveMouseHook()
    {
        if (_mouseHandle != 0)
        {
            User32.UnhookWindowsHookEx(_mouseHandle);
            _mouseHandle = 0;
        }
    }

    private static nint Install(int hookId, User32.HookProc proc) => User32.SetWindowsHookEx(
        hookId,
        Marshal.GetFunctionPointerForDelegate(proc),
        Marshal.GetHINSTANCE(typeof(KeyboardHook).Module),
        0);
}
