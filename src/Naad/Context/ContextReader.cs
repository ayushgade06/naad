using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Naad.Insertion;
using Naad.Native;

namespace Naad.Context;

/// <param name="ReadNearby">Read the text around the cursor as spelling hints.</param>
/// <param name="ExcludedApps">Process names whose text is never read.</param>
/// <param name="Edit">When the selection is read for Edit Mode.</param>
/// <param name="Screenshot">Capture the window for a cloud vision model.</param>
/// <param name="ScreenshotSize">The screenshot's longest side, in pixels.</param>
/// <param name="WholeDesktop">Capture the desktop when the window can't be captured.</param>
/// <param name="EditKey">The key held with the shortcut for a Manual edit.</param>
/// <param name="WindowTitle">Read the window's title too; off by default, since titles hold names.</param>
internal sealed record ContextOptions(
    bool ReadNearby,
    IReadOnlyList<string> ExcludedApps,
    EditMode Edit,
    bool Screenshot,
    int ScreenshotSize,
    bool WholeDesktop,
    EditModeKey EditKey = EditModeKey.Shift,
    bool WindowTitle = false);

/// <summary>
/// Reads what Naad may know about the app being dictated into, once, as the dictation starts:
/// its name, the text around the cursor (as spelling hints), and the selection for Edit Mode. UI
/// Automation reads only the focused element of the target window, never a password field or an
/// excluded app, and gives up after <see cref="Budget"/>. Nothing read is ever logged; only
/// counts and timings are. Call on the UI thread.
/// </summary>
/// <param name="options">The settings in use, read at each dictation.</param>
/// <param name="clipboard">For Manual Edit Mode's Ctrl+C fallback.</param>
internal sealed class ContextReader(Func<ContextOptions> options, IClipboard clipboard)
{
    /// <summary>A read that takes longer is skipped, so a slow app never delays the dictation.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(150);

    /// <summary>The most text read around the cursor, half before and half after it.</summary>
    public const int NearbyCharacters = 2000;

    // How long a slow app's Ctrl+C is waited for, only to put the clipboard back.
    private static readonly TimeSpan LateCopy = TimeSpan.FromSeconds(1);

    // A long selection is a document, not a sentence to rewrite; this keeps the prompt and the
    // history record a sensible size.
    private const int MaxSelectionCharacters = 20_000;

    // Long enough for a busy window to draw itself; the summary runs while the user speaks anyway.
    private static readonly TimeSpan CaptureBudget = TimeSpan.FromSeconds(1);

    // Window titles are short; a longer one is cut.
    private const int MaxTitleCharacters = 200;

    /// <summary>
    /// The user's intent from the Edit Mode setting and the key held now, read at once (no UI
    /// Automation), so a Manual edit can be checked before recording starts.
    /// </summary>
    public EditIntent Intent()
    {
        var o = options();
        return EditModeDecision.Decide(o.Edit, TextInserter.ModifierDown(o.EditKey switch
        {
            EditModeKey.Ctrl => User32.VkControl,
            _ => User32.VkShift,
        }));
    }

    /// <summary>
    /// Whether <paramref name="app"/>, a process name such as "keepass", is one the user excluded.
    /// Names match in any case, with or without ".exe".
    /// </summary>
    public static bool IsExcluded(string? app, IReadOnlyList<string> excluded) =>
        app is not null && excluded.Any(e => string.Equals(
            e.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? e[..^4] : e,
            app,
            StringComparison.OrdinalIgnoreCase));

    public async Task<DictationContext> ReadAsync(WindowTarget target)
    {
        var o = options();
        var started = Stopwatch.GetTimestamp();
        var app = AppName(target.ProcessId);
        var intent = Intent();
        var excluded = IsExcluded(app, o.ExcludedApps);

        // A screenshot needs the password check too, so the focused field is read for it as well.
        var wantsText = !excluded && (o.ReadNearby || o.Screenshot || intent != EditIntent.Dictate);
        var read = (Nearby: (string?)null, Selection: (string?)null, Password: false);
        var checkedField = false;
        if (wantsText)
        {
            try
            {
                read = await Task.Run(() => ReadFocused(target.ProcessId, o.ReadNearby))
                    .WaitAsync(Budget);
                checkedField = true;
            }
            catch (TimeoutException)
            {
                Log.Info(LogEvent.ContextReadSkipped, ("reason", 1));
            }
            catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException
                or System.Runtime.InteropServices.COMException)
            {
                Log.Info(LogEvent.ContextReadSkipped, ("reason", 2));
            }
        }

        string? selection = null;
        if (intent != EditIntent.Dictate && !excluded && !read.Password)
        {
            selection = read.Selection;

            // Only on request: in a terminal, Ctrl+C would stop the running program.
            if (selection is null && intent == EditIntent.EditRequired)
            {
                selection = await CopySelectionAsync();
            }
        }

        // Only once the focused field is known not to be a password: a read that timed out says nothing.
        var screenshot = o.Screenshot && !excluded && checkedField && !read.Password
            ? await CaptureWithinAsync(target, o)
            : null;
        var title = o.WindowTitle && !excluded ? User32.WindowTitle(target.Handle, MaxTitleCharacters) : "";
        Log.Info(
            LogEvent.ContextRead,
            ("nearby_chars", read.Nearby?.Length ?? 0),
            ("selection_chars", selection?.Length ?? 0),
            ("screenshot", screenshot is null ? 0 : 1),
            ("title", title.Length > 0 ? 1 : 0),
            ("read_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        var selected = string.IsNullOrWhiteSpace(selection) ? null : selection;

        // An excluded app's name isn't sent either; the history learns it from the window itself.
        return new(
            excluded ? null : app,
            o.ReadNearby && !read.Password ? read.Nearby : null,
            selected,
            screenshot,
            NeedsSelection: intent == EditIntent.EditRequired && selected is null,
            WindowTitle: title.Length > 0 ? title : null,
            Invocation: selected is null ? null
                : intent == EditIntent.EditRequired ? EditInvocation.Manual
                : EditInvocation.Automatic);
    }

    /// <summary>The text around a caret at <paramref name="caret"/> in <paramref name="text"/>, at most the
    /// limit.</summary>
    public static string Around(string text, int caret) => Around(text, caret, NearbyCharacters);

    /// <summary>The text around <paramref name="caret"/>, at most <paramref name="limit"/> characters.</summary>
    public static string Around(string text, int caret, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        var start = Math.Clamp(caret - (limit / 2), 0, text.Length - limit);
        return text.Substring(start, limit);
    }

    private static string? AppName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // On a thread-pool thread: UI Automation calls into the app and can block.
    private static (string? Nearby, string? Selection, bool Password) ReadFocused(uint processId, bool nearby)
    {
        var element = AutomationElement.FocusedElement;
        if (element is null || element.Current.ProcessId != processId)
        {
            return (null, null, false);
        }

        if (element.Current.IsPassword)
        {
            return (null, null, true);
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) && pattern is TextPattern text)
        {
            var selections = text.GetSelection();
            var selected = selections.Length > 0 ? selections[0].GetText(MaxSelectionCharacters) : null;
            string? around = null;
            if (nearby && selections.Length > 0)
            {
                var range = selections[0].Clone();
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -(NearbyCharacters / 2));
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, NearbyCharacters / 2);
                around = range.GetText(NearbyCharacters);
            }

            return (around, string.IsNullOrEmpty(selected) ? null : selected, false);
        }

        if (nearby && element.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern field)
        {
            var all = field.Current.Value ?? "";
            return (Around(all, all.Length), null, false);
        }

        return (null, null, false);
    }

    // Ctrl+C, then the clipboard as it was. The copied text is read only from memory.
    private async Task<string?> CopySelectionAsync()
    {
        var before = await clipboard.CaptureAsync();
        var sequence = clipboard.SequenceNumber;
        if (before is null || !TextInserter.SendCopy())
        {
            return null;
        }

        var deadline = Stopwatch.GetTimestamp();
        while (clipboard.SequenceNumber == sequence && Stopwatch.GetElapsedTime(deadline) < Budget)
        {
            await Task.Delay(10);
        }

        string? text = null;
        if (clipboard.SequenceNumber != sequence)
        {
            text = (await clipboard.CaptureAsync())?.Text;
        }
        else
        {
            // Too slow to use, but a late copy must still not replace what you had copied.
            while (clipboard.SequenceNumber == sequence && Stopwatch.GetElapsedTime(deadline) < LateCopy)
            {
                await Task.Delay(25);
            }

            if (clipboard.SequenceNumber == sequence)
            {
                return null;
            }
        }

        if (!await clipboard.RestoreAsync(before))
        {
            Log.Warn(LogEvent.ClipboardRestoreFailed);
        }

        Log.Info(LogEvent.SelectionRead, ("chars", text?.Length ?? 0));
        return text;
    }

    // A window that doesn't draw itself for PrintWindow in time (it hangs) gives no screenshot.
    private static async Task<byte[]?> CaptureWithinAsync(WindowTarget target, ContextOptions o)
    {
        try
        {
            return await Task.Run(() => Capture(target, o)).WaitAsync(CaptureBudget);
        }
        catch (TimeoutException)
        {
            Log.Info(LogEvent.ContextReadSkipped, ("reason", 3));
            return null;
        }
    }

    // The window alone, as Windows draws it for itself, so nothing lying on top of it (another app,
    // Naad's overlay) is captured; the desktop only when that fails and the user allowed it. A
    // JPEG under ScreenshotEncoder's cap.
    // ponytail: some old apps draw nothing into PrintWindow; the desktop fallback covers them if allowed.
    private static byte[]? Capture(WindowTarget target, ContextOptions o)
    {
        try
        {
            // A minimised window would give only its caption strip.
            if (User32.IsIconic(target.Handle))
            {
                return null;
            }

            if (User32.GetWindowRect(target.Handle, out var r) && r.Right > r.Left && r.Bottom > r.Top)
            {
                using var window = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
                bool printed;
                using (var graphics = Graphics.FromImage(window))
                {
                    var dc = graphics.GetHdc();
                    try
                    {
                        printed = User32.PrintWindow(target.Handle, dc, User32.PwRenderFullContent);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(dc);
                    }
                }

                if (printed)
                {
                    return ScreenshotEncoder.Jpeg(window, o.ScreenshotSize);
                }
            }

            if (!o.WholeDesktop)
            {
                return null;
            }

            var desktop = SystemInformation.VirtualScreen;
            using var shot = new Bitmap(desktop.Width, desktop.Height);
            using (var graphics = Graphics.FromImage(shot))
            {
                graphics.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size);
            }

            return ScreenshotEncoder.Jpeg(shot, o.ScreenshotSize);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            Log.Error(LogEvent.ContextReadSkipped, e);
            return null;
        }
    }
}
