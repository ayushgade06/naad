namespace Naad.Transcription;

/// <summary>ggml's kinds of device (ggml_backend_dev_type in ggml-backend.h).</summary>
internal enum GgmlDeviceType
{
    Cpu = 0,
    Gpu = 1,
    IntegratedGpu = 2,
    Accelerator = 3,
}

/// <summary>Which graphics card whisper.cpp runs on. Pure: it decides from ggml's device list.</summary>
internal static class GpuChoice
{
    /// <summary>
    /// whisper.cpp's number for the first discrete graphics card, or 0 when there is none.
    /// whisper.cpp numbers discrete and integrated cards together, in ggml's order, and takes
    /// number 0 unless told otherwise. On a laptop with both, that is usually the integrated
    /// card, which ran large-v3-turbo 3 to 10 times slower than the discrete one.
    /// </summary>
    public static int Pick(IEnumerable<GgmlDeviceType> devices)
    {
        var number = 0;
        foreach (var device in devices)
        {
            if (device == GgmlDeviceType.Gpu)
            {
                return number;
            }

            if (device == GgmlDeviceType.IntegratedGpu)
            {
                number++;
            }
        }

        return 0;
    }
}
