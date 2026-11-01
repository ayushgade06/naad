using System.Drawing.Imaging;

namespace Naad.Context;

/// <summary>
/// Turns a window capture into a JPEG small enough to send with every dictation: no larger than
/// the chosen size on its longest side, and under <see cref="MaxBytes"/>, giving up quality before
/// size. Needs no window or screen, so it is tested with drawn bitmaps.
/// </summary>
internal static class ScreenshotEncoder
{
    /// <summary>Larger images slow the request more than they help the summary.</summary>
    public const int MaxBytes = 500_000;

    // Text stays readable at these; below them a smaller image reads better than a blurrier one.
    private static readonly long[] Qualities = [75, 55, 35];
    private static readonly double[] Shrink = [1.0, 0.75, 0.5];

    private static readonly ImageCodecInfo Codec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// <summary>
    /// The first JPEG under <paramref name="maxBytes"/>, trying each size at each quality, or null
    /// when even the smallest is too large.
    /// </summary>
    /// <param name="shot">The capture.</param>
    /// <param name="longestSide">The chosen size, in pixels, of the image's longest side.</param>
    /// <param name="maxBytes">The cap; tests lower it.</param>
    public static byte[]? Jpeg(Bitmap shot, int longestSide, int maxBytes = MaxBytes)
    {
        var fit = Math.Min(1.0, longestSide / (double)Math.Max(shot.Width, shot.Height));
        foreach (var shrink in Shrink)
        {
            var width = Math.Max(1, (int)(shot.Width * fit * shrink));
            var height = Math.Max(1, (int)(shot.Height * fit * shrink));
            using var sized = new Bitmap(shot, width, height);
            foreach (var quality in Qualities)
            {
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                using var jpeg = new MemoryStream();
                sized.Save(jpeg, Codec, parameters);
                if (jpeg.Length <= maxBytes)
                {
                    return jpeg.ToArray();
                }
            }
        }

        return null;
    }
}
