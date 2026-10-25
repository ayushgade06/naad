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
internal readonly record struct KeyOutcome(IReadOnlyList<ChordEvent> Events, bool Swallow);

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
/// </list>
/// </summary>
internal sealed class ChordMatcher(IReadOnlyList<Chord> chords)
{
    private readonly HashSet<Key> _held = [];

    // Keys whose key-down was swallowed. Their repeats and key-up are swallowed too, even after
    // the chord ends, so an app never sees half of a key press.
    private readonly HashSet<Key> _swallowed = [];
    private Chord? _active;

    /// <summary>Whether a chord is active, so the hook knows when to watch the mouse.</summary>
    public bool IsActive => _active is not null;

    /// <summary>Feeds one physical key event.</summary>
    // ponytail: a key-up lost while the secure desktop (Ctrl+Alt+Del, UAC) was shown leaves the
    // key marked held, so its next press is taken as auto-repeat; P19's hook health check can resync.
    public KeyOutcome OnKey(Key key, bool down) => down ? OnKeyDown(key) : OnKeyUp(key);

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
            return new([], _swallowed.Contains(key));
        }

        var events = new List<ChordEvent>(2);
        if (_active is not null)
        {
            events.Add(new(ChordEventKind.Cancelled, _active));
            _active = null;
        }

        Chord? best = null;
        foreach (var chord in chords)
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

        // A key-up without a key-down: the key was held when the hook started.
        if (!_held.Remove(key) || _active is null || !_active.Contains(key))
        {
            return new([], swallow);
        }

        var released = new ChordEvent(ChordEventKind.Released, _active);
        _active = null;
        return new([released], swallow);
    }
}
