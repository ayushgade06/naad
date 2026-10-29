using Key = System.Windows.Forms.Keys;

namespace Naad.Hotkey;

internal enum ChordEventKind
{
    /// <summary>The chord became active: its last key went down.</summary>
    Pressed,

    /// <summary>The active chord ended because one of its keys was released.</summary>
    Released,

    /// <summary>
    /// The active chord ended because another key went down, as in Right Ctrl+C. Its
    /// keys may still be held; it becomes active again only when one of them is pressed anew.
    /// </summary>
    Cancelled,
}

internal readonly record struct ChordEvent(ChordEventKind Kind, Chord Chord);

/// <param name="Events">The chord events this key event caused, in order; usually none.</param>
/// <param name="Swallow">Keep the key event from reaching apps.</param>
/// <param name="Capture">While capturing a new shortcut, where the capture stands; otherwise null.</param>
internal readonly record struct KeyOutcome(IReadOnlyList<ChordEvent> Events, bool Swallow, CaptureStep? Capture = null);

/// <summary>
/// Turns physical key presses into chord events. Pure: it never calls Windows, so every rule
/// is unit tested. At most one chord is active at a time.
/// <list type="bullet">
/// <item>A chord becomes active when one of its keys goes down and all its keys are held.
/// Keys held before that, such as Shift while typing, don't stop it.</item>
/// <item>When several chords match, the one with the most keys wins; among equally long
/// ones, the first in the list. So with Right Ctrl and Right Ctrl+Right Shift configured,
/// adding Right Shift cancels Right Ctrl and presses the longer chord.</item>
/// <item>Any other key going down while a chord is active cancels it.</item>
/// <item>Releasing any key of the active chord releases it, in whatever order the keys come up.
/// Releasing a key never activates a chord.</item>
/// <item>A key-down for a key already held is auto-repeat and is ignored.</item>
/// <item>A mouse button or wheel used while a chord is active cancels it, so Ctrl+click and
/// Ctrl+scroll are not dictation.</item>
/// <item>A chord's non-modifier keys (Space in Left Ctrl+Space) are swallowed: the key-down that
/// activates the chord, its repeats and its key-up never reach apps. Modifiers always pass
/// through, so Right Ctrl+C still copies and no app is left seeing a modifier held.</item>
/// <item>While capturing a new shortcut (<see cref="BeginCapture"/>), keys go to a
/// <see cref="ChordCapture"/> instead: no chord events at all, and every key pressed during the
/// capture is swallowed, key-up included, so no app reacts to the keys being tried.</item>
/// </list>
/// </summary>
internal sealed class ChordMatcher(IReadOnlyList<Chord> chords)
{
    private readonly HashSet<Key> _held = [];
    private IReadOnlyList<Chord> _chords = chords;

    // Keys whose key-down was swallowed. Their repeats and key-up are swallowed too, even after
    // the chord ends, so an app never sees half of a key press.
    private readonly HashSet<Key> _swallowed = [];
    private Chord? _active;
    private ChordCapture? _capture;

    /// <summary>Whether a chord is active, so the hook knows when to watch the mouse.</summary>
    public bool IsActive => _active is not null;

    /// <summary>Whether keys go to a shortcut capture rather than to the chords.</summary>
    public bool IsCapturing => _capture is not null;

    /// <summary>
    /// Sends keys to a new <see cref="ChordCapture"/> until it captures, rejects or cancels. An
    /// active chord ends without an event, so call it only while no dictation runs.
    /// </summary>
    public void BeginCapture()
    {
        _capture = new();
        _active = null;
    }

    /// <summary>Ends a capture early, as when the settings window closes; chords match again.</summary>
    public void CancelCapture() => _capture = null;

    /// <summary>Feeds one physical key event.</summary>
    // ponytail: a key-up lost while the secure desktop (Ctrl+Alt+Del, UAC) was shown leaves the
    // key marked held, so its next press is taken as auto-repeat; P19's hook health check can resync.
    public KeyOutcome OnKey(Key key, bool down) => down ? OnKeyDown(key) : OnKeyUp(key);

    /// <summary>
    /// Matches <paramref name="replacement"/> from now on. An active chord ends without an event,
    /// so call it only while no dictation runs. Keys stay tracked as held, so a key whose key-down
    /// was swallowed still has its key-up swallowed.
    /// </summary>
    public void SetChords(IReadOnlyList<Chord> replacement)
    {
        _chords = replacement;
        _active = null;
    }

    /// <summary>Feeds a mouse button press or wheel turn.</summary>
    public IReadOnlyList<ChordEvent> OnMouseButton()
    {
        if (_active is null)
        {
            return [];
        }

        var cancelled = new ChordEvent(ChordEventKind.Cancelled, _active);
        _active = null;
        return [cancelled];
    }

    private KeyOutcome OnKeyDown(Key key)
    {
        if (!_held.Add(key))
        {
            return new([], _swallowed.Contains(key), _capture?.OnKey(key, down: true));
        }

        if (_capture is not null)
        {
            _swallowed.Add(key);
            return Captured(_capture.OnKey(key, down: true), swallow: true);
        }

        var events = new List<ChordEvent>(2);
        if (_active is not null)
        {
            events.Add(new(ChordEventKind.Cancelled, _active));
            _active = null;
        }

        Chord? best = null;
        foreach (var chord in _chords)
        {
            if (chord.Contains(key) && chord.Keys.All(_held.Contains) && chord.Keys.Count > (best?.Keys.Count ?? 0))
            {
                best = chord;
            }
        }

        var swallow = false;
        if (best is not null)
        {
            _active = best;
            events.Add(new(ChordEventKind.Pressed, best));
            swallow = !Chord.IsModifier(key) && _swallowed.Add(key);
        }

        return new(events, swallow);
    }

    private KeyOutcome OnKeyUp(Key key)
    {
        var swallow = _swallowed.Remove(key);
        if (_capture is not null)
        {
            _held.Remove(key);
            return Captured(_capture.OnKey(key, down: false), swallow);
        }

        // A key-up without a key-down: the key was held when the hook started.
        if (!_held.Remove(key) || _active is null || !_active.Contains(key))
        {
            return new([], swallow);
        }

        var released = new ChordEvent(ChordEventKind.Released, _active);
        _active = null;
        return new([released], swallow);
    }

    // A finished capture hands the keys back to the chords.
    private KeyOutcome Captured(CaptureStep step, bool swallow)
    {
        if (step.Status != CaptureStatus.Capturing)
        {
            _capture = null;
        }

        return new([], swallow, step);
    }
}
