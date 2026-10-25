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
/// </list>
/// </summary>
internal sealed class ChordMatcher(IReadOnlyList<Chord> chords)
{
    private readonly HashSet<Key> _held = [];
    private Chord? _active;

    /// <summary>Feeds one physical key event. Returns the resulting events in order, usually none.</summary>
    // ponytail: a key-up lost while the secure desktop (Ctrl+Alt+Del, UAC) was shown leaves the
    // key marked held, so its next press is taken as auto-repeat; P19's hook health check can resync.
    public IReadOnlyList<ChordEvent> OnKey(Key key, bool down) => down ? OnKeyDown(key) : OnKeyUp(key);

    private List<ChordEvent> OnKeyDown(Key key)
    {
        if (!_held.Add(key))
        {
            return [];
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

        if (best is not null)
        {
            _active = best;
            events.Add(new(ChordEventKind.Pressed, best));
        }

        return events;
    }

    private IReadOnlyList<ChordEvent> OnKeyUp(Key key)
    {
        // A key-up without a key-down: the key was held when the hook started.
        if (!_held.Remove(key) || _active is null || !_active.Contains(key))
        {
            return [];
        }

        var released = new ChordEvent(ChordEventKind.Released, _active);
        _active = null;
        return [released];
    }
}
