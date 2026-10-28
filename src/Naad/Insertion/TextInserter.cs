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

    /// <summary>The clipboard couldn't be read or written, so nothing was pasted.</summary>
    ClipboardUnavailable,

    /// <summary>Windows rejected the Ctrl+V input; the user's clipboard was put back.</summary>
    PasteRejected,

    /// <summary>Cancelled before the paste; the user's clipboard was put back.</summary>
    Cancelled,

    /// <summary>Another insertion was still running; nothing was done.</summary>
    Busy,
}

/// <summary>
/// Puts text into the focused app through the clipboard and a synthetic Ctrl+V, then puts the
/// user's clipboard back. The clipboard is shared with every app, so the original is restored
/// only if nothing else wrote to it after Naad did; newer content the user copied always wins.
/// Call on the UI thread.
/// </summary>
internal sealed class TextInserter(
    IClipboard clipboard,
    Func<bool> anyModifierDown,
    Func<bool> sendPaste,
    TimeSpan? restoreDelay = null)
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
    /// Cancellation after the paste still restores the clipboard, at once. <paramref name="pasted"/>
    /// is called as soon as Ctrl+V is sent, before the restore delay.
    /// </summary>
    public async Task<InsertionOutcome> PasteAsync(string text, Action? pasted, CancellationToken cancellationToken)
    {
        if (_busy)
        {
            return InsertionOutcome.Busy;
        }

        _busy = true;
        try
        {
            return await PasteCoreAsync(text, pasted, cancellationToken);
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
    public async Task<bool> CopyAsync(string text) => await clipboard.SetTextAsync(text) is not null;

    /// <summary>Whether Shift, Ctrl, Alt or Windows is physically held.</summary>
    public static bool AnyModifierDown() => Modifiers.Any(IsDown);

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

    private async Task<InsertionOutcome> PasteCoreAsync(
        string text, Action? pasted, CancellationToken cancellationToken)
    {
        // Waiting first means an abandoned paste never touches the clipboard. A held modifier
        // would turn Ctrl+V into Ctrl+Shift+V or Ctrl+Alt+V.
        if (!await WaitForModifiersReleasedAsync(cancellationToken))
        {
            return cancellationToken.IsCancellationRequested
                ? InsertionOutcome.Cancelled
                : InsertionOutcome.ModifiersHeld;
        }

        if (await clipboard.CaptureAsync() is not { } original)
        {
            return InsertionOutcome.ClipboardUnavailable;
        }

        LastSkippedFormats = original.Skipped;
        if (await clipboard.SetTextAsync(text) is not { } written)
        {
            // The failed write may have emptied the clipboard already.
            await clipboard.RestoreAsync(original);
            return InsertionOutcome.ClipboardUnavailable;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await RestoreIfUnchangedAsync(original, written);
            return InsertionOutcome.Cancelled;
        }

        if (!sendPaste())
        {
            await RestoreIfUnchangedAsync(original, written);
            return InsertionOutcome.PasteRejected;
        }

        pasted?.Invoke();

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

    // Null when the clipboard changed since Naad wrote it: that content is newer and stays.
    private async Task<bool?> RestoreIfUnchangedAsync(ClipboardSnapshot original, uint written) =>
        clipboard.SequenceNumber != written ? null : await clipboard.RestoreAsync(original);

    private async Task<bool> WaitForModifiersReleasedAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ModifierReleaseTimeout;
        while (anyModifierDown())
        {
            if (DateTime.UtcNow >= deadline || cancellationToken.IsCancellationRequested)
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
