using System.Runtime.InteropServices;
using Naad.Transcription;

namespace Naad.Native;

/// <summary>
/// ggml's device list, read from the native libraries that Whisper.net or LLamaSharp has already
/// loaded. Neither exposes it, and whisper.cpp must be told which graphics card to use.
/// </summary>
internal static class Ggml
{
    /// <summary>
    /// Whisper.net's ggml: the kind of each device, in ggml's order. Empty when the libraries
    /// aren't loaded yet or lack these exports, as after an update that renames them.
    /// </summary>
    public static GgmlDeviceType[] DeviceTypes() => DeviceTypes("ggml-whisper.dll", "ggml-base-whisper.dll");

    /// <summary>LLamaSharp's ggml, a separate copy under the plain names; as <see cref="DeviceTypes()"/>.</summary>
    public static GgmlDeviceType[] LlamaDeviceTypes() => DeviceTypes("ggml.dll", "ggml-base.dll");

    // The registry functions are in one library and the device functions in its base library.
    private static unsafe GgmlDeviceType[] DeviceTypes(string registryLibrary, string backendLibrary)
    {
        var registry = Kernel32.GetModuleHandle(registryLibrary);
        var backend = Kernel32.GetModuleHandle(backendLibrary);
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
