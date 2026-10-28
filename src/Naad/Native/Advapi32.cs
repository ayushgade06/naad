using System.Runtime.InteropServices;

namespace Naad.Native;

/// <summary>advapi32.dll declarations: reading a process's integrity level.</summary>
internal static partial class Advapi32
{
    public const uint TokenQuery = 0x0008;

    // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel; the data is a TOKEN_MANDATORY_LABEL whose
    // first field points to the integrity SID.
    public const int TokenIntegrityLevel = 25;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(
        nint tokenHandle, int tokenInformationClass, nint tokenInformation, uint tokenInformationLength,
        out uint returnLength);

    [LibraryImport("advapi32.dll")]
    public static partial nint GetSidSubAuthorityCount(nint pSid);

    [LibraryImport("advapi32.dll")]
    public static partial nint GetSidSubAuthority(nint pSid, uint nSubAuthority);
}
