using System.Globalization;
using Naad.Dictation;
using Naad.Hotkey;

namespace Naad.Tray;

/// <summary>
/// The menu's status line and recent problems, and the check that flashes on the icon after words
/// go into the dictionary. Use on the UI thread.
/// </summary>
internal sealed partial class TrayIcon
{
    // Long enough to notice in the corner of the screen, short enough not to be mistaken for a state.
    private const int CheckFlashMilliseconds = 2_000;

    // A darker green, so the check reads on light and dark taskbars alike, inside the white ring.
    private static readonly Color CheckColor = Color.FromArgb(0x2E, 0x7D, 0x32);

    private readonly string _version = Window.Diagnostics.Build();
    private readonly List<ToolStripItem> _errorItems = [];
    private IReadOnlyList<RecentError> _errors = [];
    private DictationState _state = DictationState.Idle;
    private Icon? _checkImage;
    private System.Windows.Forms.Timer? _checkFlash;

    /// <summary>The menu's first line: what Naad is doing, and its version.</summary>
    public static string StatusLine(DictationState state, string idleTooltip, string version) => state switch
    {
        DictationState.Recording => $"Recording… (Naad {version})",
        DictationState.Transcribing or DictationState.Inserting => $"Transcribing… (Naad {version})",
        _ => idleTooltip.StartsWith("Naad:", StringComparison.Ordinal)
            ? $"Naad {version}:{idleTooltip["Naad:".Length..]}"
            : idleTooltip,
    };

    /// <summary>Paste again, its shortcut when it has one, and a preview of what it pastes.</summary>
    public static string PasteAgainLabel(string? preview, Chord? shortcut)
    {
        var label = shortcut is { } keys ? $"Paste again ({keys.Describe()})" : "Paste again";
        return preview is null ? label : $"{label}: “{preview}”";
    }

    // A warning shown to the user goes into the menu too, by its fixed title only.
    private void RememberError(string title)
    {
        _errors = RecentErrors.Add(_errors, new(title, DateTimeOffset.Now));
        foreach (var item in _errorItems)
        {
            _menu.Items.Remove(item);
            item.Dispose();
        }

        _errorItems.Clear();
        var at = _menu.Items.IndexOf(_status) + 1;
        foreach (var error in _errors)
        {
            var time = error.Time.ToString("t", CultureInfo.CurrentCulture);
            var item = new ToolStripMenuItem($"{time}  {error.Title}") { Enabled = false };
            _errorItems.Add(item);
            _menu.Items.Insert(at++, item);
        }
    }

    // A green check for two seconds; never over the recording or transcribing dot.
    private void FlashCheck()
    {
        if (_state != DictationState.Idle)
        {
            return;
        }

        _checkImage ??= WithDot(_image, CheckColor);
        _icon.Icon = _checkImage;
        _checkFlash?.Dispose();
        _checkFlash = new System.Windows.Forms.Timer { Interval = CheckFlashMilliseconds };
        _checkFlash.Tick += (_, _) =>
        {
            _checkFlash?.Dispose();
            _checkFlash = null;
            if (_state == DictationState.Idle)
            {
                _icon.Icon = _image;
            }
        };
        _checkFlash.Start();
    }
}
