using Key = System.Windows.Forms.Keys;

namespace Naad.Hotkey;

internal enum CaptureStatus
{
    /// <summary>Still waiting for the keys to be released.</summary>
    Capturing,

    /// <summary>A key came up; <see cref="CaptureStep.Chord"/> holds what was pressed.</summary>
    Captured,

    /// <summary>Esc was pressed.</summary>
    Cancelled,

    /// <summary>The keys pressed can't make a chord, such as a key a hook never reports.</summary>
    Rejected,
}

/// <param name="Status">Where the capture stands.</param>
/// <param name="Chord">The captured chord when <paramref name="Status"/> is Captured; otherwise null.</param>
internal readonly record struct CaptureStep(CaptureStatus Status, Chord? Chord = null);

/// <summary>
/// Turns the keys pressed while the settings window listens into a chord, the way Windows'
/// own shortcut boxes do: hold the keys, and the chord is taken when the first one comes up.
/// Pure: it never calls Windows.
/// <list type="bullet">
/// <item>Keys held before capture started are ignored, including their key-ups.</item>
/// <item>A key-down for a key already held is auto-repeat and is ignored.</item>
/// <item>Esc cancels, alone or with other keys, so Esc can never be part of a shortcut.</item>
/// <item>Keys after the fourth are ignored: a chord holds at most <see cref="Chord.MaxKeys"/>.</item>
/// </list>
/// </summary>
internal sealed class ChordCapture
{
    private readonly List<Key> _pressed = [];
    private readonly HashSet<Key> _held = [];

    public CaptureStep OnKey(Key key, bool down)
    {
        if (down)
        {
            if (!_held.Add(key))
            {
                return new(CaptureStatus.Capturing);
            }

            if (key == Key.Escape)
            {
                return new(CaptureStatus.Cancelled);
            }

            if (_pressed.Count < Chord.MaxKeys)
            {
                _pressed.Add(key);
            }

            return new(CaptureStatus.Capturing);
        }

        if (!_held.Remove(key) || !_pressed.Contains(key))
        {
            return new(CaptureStatus.Capturing);
        }

        return Chord.TryCreate(_pressed, out var chord)
            ? new(CaptureStatus.Captured, chord)
            : new(CaptureStatus.Rejected);
    }
}
