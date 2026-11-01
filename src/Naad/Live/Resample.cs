using System.Buffers.Binary;

namespace Naad.Live;

/// <summary>Converts the recorder's 16 kHz audio for a service that wants 24 kHz. Pure.</summary>
internal static class Resample
{
    /// <summary>
    /// 16 kHz mono PCM16 as 24 kHz: three samples for every two, the new ones on a straight line
    /// between their neighbours. Each buffer is converted on its own; the last sample of a buffer
    /// is held rather than joined to the next, a difference no transcriber hears. The recorder's
    /// buffers hold an even number of samples; an odd last one would be dropped.
    /// </summary>
    public static byte[] To24k(ReadOnlySpan<byte> pcm)
    {
        var count = pcm.Length / 2;
        var output = new byte[count / 2 * 3 * 2];
        for (var i = 0; i < count / 2; i++)
        {
            int a = Sample(pcm, 2 * i);
            int b = Sample(pcm, (2 * i) + 1);
            int c = (2 * i) + 2 < count ? Sample(pcm, (2 * i) + 2) : b;
            Write(output, 3 * i, a);
            Write(output, (3 * i) + 1, a + ((b - a) * 2 / 3));
            Write(output, (3 * i) + 2, b + ((c - b) / 3));
        }

        return output;
    }

    private static short Sample(ReadOnlySpan<byte> pcm, int index) =>
        BinaryPrimitives.ReadInt16LittleEndian(pcm[(index * 2)..]);

    private static void Write(byte[] output, int index, int sample) =>
        BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(index * 2), (short)sample);
}
