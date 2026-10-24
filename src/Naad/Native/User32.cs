using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>user32.dll declarations. Every P/Invoke in Naad lives in this folder.</summary>
internal static partial class User32
{
    public const int WhKeyboardLl = 13;

    public const int WmKeyDown = 0x0100;
    public const int WmSysKeyDown = 0x0104;

    // Set on events produced by SendInput, including Naad's own Ctrl+V.
    public const uint LlkhfInjected = 0x10;

    public const ushort VkRControl = 0xA3;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial nint SetWindowsHookEx(int idHook, nint lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

}
