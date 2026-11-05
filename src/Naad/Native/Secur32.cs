using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>secur32.dll declarations: the signed-in user's display name, for the Home greeting.</summary>
internal static partial class Secur32
{
    // EXTENDED_NAME_FORMAT.NameDisplay: the full name, such as "Ada Lovelace", where Windows has one.
    private const int NameDisplay = 3;

    // Display names are short; a longer one is cut off, which is fine for a first name.
    private const int MaxName = 256;

    /// <summary>The user's display name, or null when Windows has none (a local account often has none).</summary>
    public static unsafe string? DisplayName()
    {
        var buffer = stackalloc char[MaxName];
        var size = (uint)MaxName;
        return GetUserNameExW(NameDisplay, buffer, ref size) && size > 0 ? new string(buffer, 0, (int)size) : null;
    }

    [LibraryImport("secur32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserNameExW(int nameFormat, char* buffer, ref uint size);
}
