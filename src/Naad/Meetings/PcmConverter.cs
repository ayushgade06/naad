using System.Buffers.Binary;
using NAudio.Wave;

namespace Naad.Meetings;

// ponytail: linear interpolation without a low-pass filter, so sound above 8 kHz folds into the
// speech band; Windows' own resampler does it properly when process loopback asks for 16 kHz, so
// this matters only on Windows 10 before 2004. Add a short FIR before decimating if it shows.
/// <summary>
/// Converts the PC's sound, as Windows mixes it (usually 48 kHz stereo 32-bit float), to the
/// 16 kHz mono 16-bit PCM Whisper takes. Pure; keeps its place between buffers, so a long
/// meeting never drifts. One caller at a time.
/// </summary>
internal sealed class PcmConverter
{
    private const int TargetRate = 16000;

    // 16-bit full scale: samples are read and written on the same scale, so PCM passes unchanged.
    private const float FullScale = 32768f;

    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly bool _float;
    private readonly double _step;

    // Where the next output sample falls, in input frames from the start of the next buffer;
    // negative when it falls between the last buffer's final frame and this one's first.
    private double _position;
    private float _previous;

    /// <exception cref="NotSupportedException">A format other than 16-bit PCM or 32-bit float.</exception>
    public PcmConverter(WaveFormat source)
    {
        _float = source.Encoding == WaveFormatEncoding.IeeeFloat
            || (source.Encoding == WaveFormatEncoding.Extensible && source.BitsPerSample == 32);
        if (!(_float && source.BitsPerSample == 32) && !(source.BitsPerSample == 16 && !_float))
        {
            throw new NotSupportedException("Only 16-bit PCM or 32-bit float audio.");
        }

        _channels = source.Channels;
        _bytesPerSample = source.BitsPerSample / 8;
        _step = (double)source.SampleRate / TargetRate;
    }

    /// <summary>The next buffer, as 16 kHz mono PCM16.</summary>
    public byte[] Convert(ReadOnlySpan<byte> buffer)
    {
        var frames = buffer.Length / (_bytesPerSample * _channels);
        if (frames == 0)
        {
            return [];
        }

        var output = new List<byte>((int)(frames / _step * 2) + 4);
        while (_position < frames - 1)
        {
            var index = (int)Math.Floor(_position);
            var fraction = (float)(_position - index);
            var a = index < 0 ? _previous : Mono(buffer, index);
            var b = Mono(buffer, index + 1);
            var sample = Math.Clamp(
                MathF.Round((a + ((b - a) * fraction)) * FullScale), short.MinValue, short.MaxValue);
            output.Add((byte)(short)sample);
            output.Add((byte)((short)sample >> 8));
            _position += _step;
        }

        _previous = Mono(buffer, frames - 1);
        _position -= frames;
        return [.. output];
    }

    // One frame's channels averaged, from -1 to 1.
    private float Mono(ReadOnlySpan<byte> buffer, int frame)
    {
        float sum = 0;
        for (var c = 0; c < _channels; c++)
        {
            var at = ((frame * _channels) + c) * _bytesPerSample;
            sum += _float
                ? BinaryPrimitives.ReadSingleLittleEndian(buffer[at..])
                : BinaryPrimitives.ReadInt16LittleEndian(buffer[at..]) / FullScale;
        }

        return sum / _channels;
    }
}
