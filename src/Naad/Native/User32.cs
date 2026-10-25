using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>user32.dll declarations. Every P/Invoke in Naad lives in this folder.</summary>
internal static partial class User32
{
    public const int WhKeyboardLl = 13;
    public const int WhMouseLl = 14;

    public const int WmKeyDown = 0x0100;
    public const int WmSysKeyDown = 0x0104;

    public const int WmLButtonDown = 0x0201;
    public const int WmRButtonDown = 0x0204;
    public const int WmMButtonDown = 0x0207;
    public const int WmMouseWheel = 0x020A;
    public const int WmXButtonDown = 0x020B;
    public const int WmMouseHWheel = 0x020E;

    // Set on events produced by SendInput, including Naad's own Ctrl+V.
    public const uint LlkhfInjected = 0x10;

    // Also set when the injecting process runs at a lower integrity level, e.g. a sandbox.
    public const uint LlkhfLowerIlInjected = 0x02;

    // The mouse hook's equivalent of LlkhfLowerIlInjected.
    public const uint LlmhfLowerIlInjected = 0x02;

    public const ushort VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const ushort VkMenu = 0x12;
    public const ushort VkLWin = 0x5B;
    public const ushort VkRWin = 0x5C;
    public const ushort VkV = 0x56;

    public const uint InputKeyboard = 1;
    public const uint KeyEventFKeyUp = 0x0002;

    public delegate nint HookProc(int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static partial nint SetWindowsHookEx(int idHook, nint lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint cInputs, [In] Input[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // MOUSEINPUT is the largest member, so it must be declared for INPUT to have
    // the size SendInput expects, even though Naad only sends keyboard input.
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
