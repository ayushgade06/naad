using Naad.Audio;

namespace Naad.Live;

/// <summary>
/// Splits 16 kHz mono PCM16 audio into pieces at the pauses between phrases, as it arrives, so
/// each piece can be transcribed while the rest is still being recorded. Pure: it only looks at
/// the samples. Not thread-safe; one caller feeds it in order.
/// </summary>
/// <param name="pause">How long a quiet stretch ends a piece.</param>
/// <param name="longest">A piece is cut here at its quietest moment even without a pause.</param>
internal sealed class SpeechChunker(TimeSpan pause, TimeSpan longest)
{
    // 0.1 s windows: short enough to find the gap between words, long enough for a steady level.
    public const int WindowBytes = 3200;

    // About -42 dBFS: below a voice at a normal distance, above a quiet room's hiss.
    public const double QuietRms = 262;

    // Under 0.3 s of voice is a cough or a click; it waits to join the next piece instead of
    // being transcribed alone, where Whisper tends to invent words for it.
    private const int MinVoicedWindows = 3;

    private readonly int _pauseWindows = Windows(pause);
    private readonly int _longestWindows = Windows(longest);
    private readonly byte[] _partial = new byte[WindowBytes];
    private readonly List<double> _levels = [];
    private int _partialLength;
    private long _position;
    private long _start;
    private int _voiced;
    private int _quietRun;

    /// <summary>The pause and length used for dictation: short pieces, so words show up soon.</summary>
    public static SpeechChunker ForDictation() => new(TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(15));

    /// <summary>Where the last finished piece ends, in bytes from the start of the audio.</summary>
    public long Cut => _start;

    /// <summary>
    /// Adds the next audio and returns the byte offsets, from the start of the audio, where pieces
    /// finished; each piece runs from the previous offset (or 0) to its own.
    /// </summary>
    public IReadOnlyList<long> Add(ReadOnlySpan<byte> pcm)
    {
        List<long>? cuts = null;
        while (pcm.Length > 0)
        {
            var take = Math.Min(WindowBytes - _partialLength, pcm.Length);
            pcm[..take].CopyTo(_partial.AsSpan(_partialLength));
            _partialLength += take;
            pcm = pcm[take..];
            if (_partialLength < WindowBytes)
            {
                break;
            }

            _partialLength = 0;
            _position += WindowBytes;
            if (Window(LevelMeter.Rms(_partial)) is { } cut)
            {
                (cuts ??= []).Add(cut);
            }
        }

        return cuts ?? (IReadOnlyList<long>)[];
    }

    private long? Window(double rms)
    {
        _levels.Add(rms);
        if (rms < QuietRms)
        {
            _quietRun++;
        }
        else
        {
            _voiced++;
            _quietRun = 0;
        }

        // Nothing said yet: the quiet is dropped from the front, so a piece starts near its words.
        if (_voiced == 0)
        {
            _start = _position;
            _levels.Clear();
            _quietRun = 0;
            return null;
        }

        if (_voiced >= MinVoicedWindows && _quietRun >= _pauseWindows)
        {
            return Finish(_position);
        }

        if (_levels.Count < _longestWindows)
        {
            return null;
        }

        // No pause for the longest piece allowed: cut at the quietest window of its second half,
        // which is most likely between words.
        var quietest = _levels.Count / 2;
        for (var i = quietest; i < _levels.Count; i++)
        {
            if (_levels[i] < _levels[quietest])
            {
                quietest = i;
            }
        }

        var end = _start + ((quietest + 1L) * WindowBytes);
        var rest = _levels.Skip(quietest + 1).ToList();
        var result = Finish(end);
        _levels.AddRange(rest);
        _voiced = rest.Count(l => l >= QuietRms);
        return result;
    }

    private long Finish(long end)
    {
        _start = end;
        _levels.Clear();
        _voiced = 0;
        _quietRun = 0;
        return end;
    }

    private static int Windows(TimeSpan duration) =>
        Math.Max(1, (int)Math.Round(duration.TotalSeconds * 10));
}
