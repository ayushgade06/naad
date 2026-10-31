namespace Naad.Tray;

/// <summary>
/// The heights of the overlay's voice bars. The newest level drives the centre bar and older
/// levels ripple outward to both edges, so speech spreads from the middle like a wave; an
/// envelope keeps the edges shorter than the centre. Pure: it only does arithmetic.
/// </summary>
internal sealed class VoiceBars
{
    // How much shorter the outermost bars are than the centre one at the same level.
    private const double EdgeDrop = 0.6;

    // Levels newest first; index i drives the two bars i places from the centre.
    private readonly double[] _recent;

    /// <param name="count">The number of bars; odd, so there is one centre bar.</param>
    public VoiceBars(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (count % 2 == 0)
        {
            throw new ArgumentException("The bar count must be odd.", nameof(count));
        }

        Count = count;
        _recent = new double[(count / 2) + 1];
    }

    public int Count { get; }

    /// <summary>Takes a new 0 to 1 level for the centre bar and moves older ones outward.</summary>
    public void Push(double level)
    {
        Array.Copy(_recent, 0, _recent, 1, _recent.Length - 1);
        _recent[0] = Math.Clamp(level, 0, 1);
    }

    /// <summary>Back to silence, for the next recording.</summary>
    public void Reset() => Array.Clear(_recent);

    /// <summary>The height of bar <paramref name="index"/> (0 is the leftmost), 0 to 1.</summary>
    public double Height(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        var distance = Math.Abs(index - (Count / 2));
        var edge = (double)distance / Math.Max(1, Count / 2);
        return _recent[distance] * (1 - (EdgeDrop * edge * edge));
    }
}
