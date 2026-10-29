using System.Runtime.InteropServices;
using Naad.Transcription;

namespace Naad.Native;

/// <summary>
/// ggml's device list, read from the whisper.cpp libraries that Whisper.net has already loaded.
/// Whisper.net doesn't expose it, and whisper.cpp must be told which graphics card to use.
/// </summary>
internal static class Ggml
{
    // Whisper.net's builds of ggml: the registry is in ggml-whisper.dll, the device functions in
    // ggml-base-whisper.dll.
    private const string Registry = "ggml-whisper.dll";
    private const string Backend = "ggml-base-whisper.dll";

    /// <summary>
    /// The kind of each device, in ggml's order. Empty when the libraries aren't loaded yet or
    /// lack these exports, as after a Whisper.net update that renames them.
    /// </summary>
    public static unsafe GgmlDeviceType[] DeviceTypes()
    {
        var registry = Kernel32.GetModuleHandle(Registry);
        var backend = Kernel32.GetModuleHandle(Backend);
        if (registry == 0 || backend == 0
            || !NativeLibrary.TryGetExport(registry, "ggml_backend_dev_count", out var count)
            || !NativeLibrary.TryGetExport(registry, "ggml_backend_dev_get", out var get)
            || !NativeLibrary.TryGetExport(backend, "ggml_backend_dev_type", out var type))
        {
            return [];
        }

        var devices = new GgmlDeviceType[(int)((delegate* unmanaged[Cdecl]<nuint>)count)()];
        for (var i = 0; i < devices.Length; i++)
        {
            var device = ((delegate* unmanaged[Cdecl]<nuint, nint>)get)((nuint)i);
            devices[i] = (GgmlDeviceType)((delegate* unmanaged[Cdecl]<nint, int>)type)(device);
        }

        return devices;
    }
}
