using NAudio.Wave;

namespace Naad.Audio;

internal static class Wav
{
    /// <summary>Wraps raw PCM in a WAV container, in memory.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> pcm, WaveFormat format)
    {
        using var output = new MemoryStream();

        // WaveFileWriter fills in the RIFF and data sizes when disposed, so it must be
        // disposed before the stream is read. It leaves a caller-supplied stream open.
        using (var writer = new WaveFileWriter(output, format))
        {
            writer.Write(pcm);
        }

        return output.ToArray();
    }
}
