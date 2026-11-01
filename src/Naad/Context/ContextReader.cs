using System.Diagnostics;
using System.Drawing.Imaging;
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
internal sealed record ContextOptions(
    bool ReadNearby,
    IReadOnlyList<string> ExcludedApps,
    EditMode Edit,
    bool Screenshot,
    int ScreenshotSize,
    bool WholeDesktop,
    EditModeKey EditKey = EditModeKey.Shift);

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

    public async Task<DictationContext> ReadAsync(WindowTarget target)
    {
        var o = options();
        var started = Stopwatch.GetTimestamp();
        var app = AppName(target.ProcessId);
        var intent = EditModeDecision.Decide(o.Edit, TextInserter.ModifierDown(o.EditKey switch
        {
            EditModeKey.Ctrl => User32.VkControl,
            _ => User32.VkShift,
        }));
        var excluded = app is not null && o.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
        var wantsText = !excluded && (o.ReadNearby || intent != EditIntent.Dictate);
        var read = (Nearby: (string?)null, Selection: (string?)null, Password: false);
        if (wantsText)
        {
            try
            {
                read = await Task.Run(() => ReadFocused(target.ProcessId, o.ReadNearby))
                    .WaitAsync(Budget);
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

        var screenshot = o.Screenshot && !excluded && !read.Password ? Capture(target, o) : null;
        Log.Info(
            LogEvent.ContextRead,
            ("nearby_chars", read.Nearby?.Length ?? 0),
            ("selection_chars", selection?.Length ?? 0),
            ("screenshot", screenshot is null ? 0 : 1),
            ("read_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        return new(
            app,
            o.ReadNearby && !read.Password ? read.Nearby : null,
            string.IsNullOrWhiteSpace(selection) ? null : selection,
            screenshot,
            NeedsSelection: intent == EditIntent.EditRequired && string.IsNullOrWhiteSpace(selection));
    }

    /// <summary>The text around a caret at <paramref name="caret"/> in <paramref name="text"/>, at most the
    /// limit.</summary>
    public static string Around(string text, int caret)
    {
        if (text.Length <= NearbyCharacters)
        {
            return text;
        }

        var start = Math.Clamp(caret - (NearbyCharacters / 2), 0, text.Length - NearbyCharacters);
        return text.Substring(start, NearbyCharacters);
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
            var selected = selections.Length > 0 ? selections[0].GetText(-1) : null;
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

    // The window (or, if asked, the desktop) as a PNG no larger than the chosen size.
    private static byte[]? Capture(WindowTarget target, ContextOptions o)
    {
        try
        {
            var bounds = User32.GetWindowRect(target.Handle, out var r) && r.Right > r.Left && r.Bottom > r.Top
                ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)
                : o.WholeDesktop ? SystemInformation.VirtualScreen : Rectangle.Empty;
            if (bounds.IsEmpty)
            {
                return null;
            }

            using var shot = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(shot))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            var scale = Math.Min(1.0, o.ScreenshotSize / (double)Math.Max(bounds.Width, bounds.Height));
            using var sized = new Bitmap(shot, (int)(bounds.Width * scale), (int)(bounds.Height * scale));
            using var png = new MemoryStream();
            sized.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException or ArgumentException)
        {
            Log.Error(LogEvent.ContextReadSkipped, e);
            return null;
        }
    }
}
