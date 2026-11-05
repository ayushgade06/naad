using System.Runtime.InteropServices;
using Naad.Hotkey;
using Naad.Native;

namespace Naad.Insertion;

internal enum InsertionOutcome
{
    /// <summary>Pasted, and the user's clipboard was put back.</summary>
    Pasted,

    /// <summary>Pasted. Someone copied something new meanwhile, so it was left in place.</summary>
    PastedClipboardChanged,

    /// <summary>Pasted, but the user's clipboard could not be put back.</summary>
    PastedRestoreFailed,

    /// <summary>A modifier was still held, so nothing was pasted and the clipboard is untouched.</summary>
    ModifiersHeld,

    /// <summary>
    /// The clipboard couldn't be used and typing the text was rejected too: nothing was inserted.
    /// </summary>
    ClipboardUnavailable,

    /// <summary>The clipboard couldn't be used, so the text was typed as Unicode keys instead.</summary>
    Typed,

    /// <summary>Typing was cut short: part of the text may have reached the window.</summary>
    TypedPartially,

    /// <summary>The target lost focus or can't take input any more; nothing was sent.</summary>
    TargetChanged,

    /// <summary>Windows rejected the Ctrl+V input; the user's clipboard was put back.</summary>
    PasteRejected,

    /// <summary>Cancelled before the paste; the user's clipboard was put back.</summary>
    Cancelled,

    /// <summary>Another insertion was still running; nothing was done.</summary>
    Busy,

    /// <summary>Pasted, and left on the clipboard: restoring is turned off.</summary>
    PastedNotRestored,
}

/// <summary>The user's clipboard choices, read at each paste.</summary>
/// <param name="Restore">Put back what was on the clipboard after a paste.</param>
/// <param name="KeepOutOfHistory">Mark Naad's text to stay out of clipboard history and cloud sync.</param>
internal readonly record struct ClipboardOptions(bool Restore, bool KeepOutOfHistory)
{
    /// <summary>Both on, as Naad has always worked.</summary>
    public static ClipboardOptions Default { get; } = new(true, true);
}

/// <summary>
/// Puts text into the focused app through the clipboard and a synthetic Ctrl+V, then puts the
/// user's clipboard back. The clipboard is shared with every app, so the original is restored
/// only if nothing else wrote to it after Naad did; newer content the user copied always wins.
/// When the clipboard can't be used at all, the text is typed as Unicode keys instead. Only
/// one of the two ever runs for a text, so it is never inserted twice. Call on the UI thread.
/// </summary>
internal sealed class TextInserter(
    IClipboard clipboard,
    Func<bool> anyModifierDown,
    Func<bool> sendPaste,
    Func<string, TypingResult> typeText,
    TimeSpan? restoreDelay = null,
    Func<ClipboardOptions>? options = null)
{
    // The target reads the clipboard when it handles the Ctrl+V, not when it is sent, and
    // Electron apps (VS Code) read it asynchronously. Restoring earlier would paste the old text.
    // ponytail: a fixed delay, not a signal; raise it if a slow app pastes the old clipboard.
    public static readonly TimeSpan DefaultRestoreDelay = TimeSpan.FromMilliseconds(500);

    // The user normally lets go within a few hundred ms of releasing the dictation key.
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    private static readonly ushort[] Modifiers =
        [User32.VkShift, User32.VkControl, User32.VkMenu, User32.VkLWin, User32.VkRWin];

    private readonly TimeSpan _restoreDelay = restoreDelay ?? DefaultRestoreDelay;
    private bool _busy;

    /// <summary>Formats the last snapshot couldn't copy, for the log; they don't come back.</summary>
    public int LastSkippedFormats { get; private set; }

    /// <summary>
    /// Pastes <paramref name="text"/>. The clipboard is touched only once no modifier is held,
    /// and every outcome except a successful paste leaves the user's clipboard as it was.
    /// Cancellation after the paste still restores the clipboard, at once.
    /// <paramref name="canInsert"/> is asked right before any input is sent, so focus moving
    /// during the wait is caught. <paramref name="pasted"/> is called as soon as the text is
    /// sent, before the restore delay.
    /// </summary>
    public async Task<InsertionOutcome> PasteAsync(
        string text, Func<bool> canInsert, Action? pasted, CancellationToken cancellationToken)
    {
        if (_busy)
        {
            return InsertionOutcome.Busy;
        }

        _busy = true;
        try
        {
            return await PasteCoreAsync(text, canInsert, pasted, cancellationToken);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Leaves <paramref name="text"/> on the clipboard for the user to paste themselves, kept out
    /// of clipboard history. Returns false if the clipboard couldn't be written.
    /// </summary>
    public async Task<bool> CopyAsync(string text) =>
        await clipboard.SetTextAsync(text, Options.KeepOutOfHistory) is not null;

    private ClipboardOptions Options => options?.Invoke() ?? ClipboardOptions.Default;

    /// <summary>Whether Shift, Ctrl, Alt or Windows is physically held.</summary>
    public static bool AnyModifierDown() => Modifiers.Any(IsDown);

    /// <summary>The modifiers held now, by side, such as Right Ctrl.</summary>
    public static IReadOnlyList<System.Windows.Forms.Keys> ModifiersDown() =>
        [.. HeldModifiers.All.Where(key => IsDown((ushort)key))];

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

    /// <summary>Whether a modifier, such as Manual Edit Mode's key, is physically held on either side.</summary>
    /// <param name="vk">VK_SHIFT or VK_CONTROL.</param>
    public static bool ModifierDown(ushort vk) => IsDown(vk);

    /// <summary>Sends Enter to the foreground window, for the "press enter" voice command.</summary>
    public static bool SendEnter() => Send([Key(User32.VkReturn, up: false), Key(User32.VkReturn, up: true)]);

    /// <summary>
    /// Sends Ctrl+C to the foreground window, to read a selection UI Automation can't. A held Shift
    /// (Manual Edit Mode's key) is lifted around it, so the app doesn't get Ctrl+Shift+C.
    /// </summary>
    public static bool SendCopy()
    {
        ushort[] held = [.. new[] { User32.VkShift }.Where(IsDown)];
        User32.Input[] copy =
        [
            Key(User32.VkControl, up: false),
            Key(User32.VkC, up: false),
            Key(User32.VkC, up: true),
            Key(User32.VkControl, up: true),
        ];
        return Send(
            [.. held.Select(vk => Key(vk, up: true)), .. copy, .. held.Select(vk => Key(vk, up: false))]);
    }

    private static bool Send(User32.Input[] inputs) =>
        User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<User32.Input>()) == inputs.Length;

    private async Task<InsertionOutcome> PasteCoreAsync(
        string text, Func<bool> canInsert, Action? pasted, CancellationToken cancellationToken)
    {
        // Waiting first means an abandoned paste never touches the clipboard. A held modifier
        // would turn Ctrl+V into Ctrl+Shift+V or Ctrl+Alt+V.
        if (!await WaitForModifiersReleasedAsync(ModifierReleaseTimeout, cancellationToken))
        {
            return cancellationToken.IsCancellationRequested
                ? InsertionOutcome.Cancelled
                : InsertionOutcome.ModifiersHeld;
        }

        if (await clipboard.CaptureAsync() is not { } original)
        {
            return TypeInstead(text, canInsert, pasted, cancellationToken);
        }

        LastSkippedFormats = original.Skipped;

        // Read once: a choice saved during the paste applies to the next one.
        var choice = Options;
        if (await clipboard.SetTextAsync(text, choice.KeepOutOfHistory) is not { } written)
        {
            // The failed write may have emptied the clipboard already.
            if (await clipboard.RestoreAsync(original) != true)
            {
                Log.Warn(LogEvent.ClipboardRestoreFailed);
            }

            return TypeInstead(text, canInsert, pasted, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await RestoreIfUnchangedAsync(original, written);
            return InsertionOutcome.Cancelled;
        }

        if (!canInsert())
        {
            await RestoreIfUnchangedAsync(original, written);
            return InsertionOutcome.TargetChanged;
        }

        if (!sendPaste())
        {
            await RestoreIfUnchangedAsync(original, written);
            return InsertionOutcome.PasteRejected;
        }

        pasted?.Invoke();

        // Restoring is off: the text stays for the user to paste again. Only after a paste: every
        // path where nothing went in still puts the user's clipboard back.
        if (!choice.Restore)
        {
            return InsertionOutcome.PastedNotRestored;
        }

        try
        {
            await Task.Delay(_restoreDelay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down: restore now rather than leave the transcript on the clipboard.
        }

        return await RestoreIfUnchangedAsync(original, written) switch
        {
            null => InsertionOutcome.PastedClipboardChanged,
            true => InsertionOutcome.Pasted,
            false => InsertionOutcome.PastedRestoreFailed,
        };
    }

    // Only reached when nothing was pasted, so typing can't duplicate text.
    private InsertionOutcome TypeInstead(
        string text, Func<bool> canInsert, Action? pasted, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return InsertionOutcome.Cancelled;
        }

        if (!canInsert())
        {
            return InsertionOutcome.TargetChanged;
        }

        switch (typeText(text))
        {
            case TypingResult.Typed:
                pasted?.Invoke();
                return InsertionOutcome.Typed;
            case TypingResult.Partial:
                return InsertionOutcome.TypedPartially;
            default:
                return InsertionOutcome.ClipboardUnavailable;
        }
    }

    // Null when the clipboard changed since Naad wrote it: that content is newer and stays.
    private Task<bool?> RestoreIfUnchangedAsync(ClipboardSnapshot original, uint written) =>
        clipboard.RestoreAsync(original, ifUnchanged: written);

    /// <summary>
    /// Waits until no modifier is physically held, for at most <paramref name="timeout"/>. Returns
    /// false if one still is, or if cancelled.
    /// </summary>
    public async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        // The monotonic clock: a wall-clock change mustn't stretch or cut the wait.
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (anyModifierDown())
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) >= timeout
                || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            await Task.Delay(PollInterval, CancellationToken.None);
        }

        return !cancellationToken.IsCancellationRequested;
    }

    // The high bit of GetAsyncKeyState is set while the key is physically down.
    private static bool IsDown(ushort vk) => User32.GetAsyncKeyState(vk) < 0;

    private static User32.Input Key(ushort vk, bool up) => new()
    {
        Type = User32.InputKeyboard,
        Union = new User32.InputUnion
        {
            Keyboard = new User32.KeyboardInput
            {
                Vk = vk,
                Flags = up ? User32.KeyEventFKeyUp : 0,

                // Marks the paste as Naad's own, so the keyboard hook skips it.
                ExtraInfo = KeyboardHook.OwnInputMarker,
            },
        },
    };
}
