using System.Runtime.InteropServices;
using Naad.Native;

namespace Naad.Insertion;

internal enum InsertionDecision
{
    /// <summary>The window the user dictated into still has focus and can receive input.</summary>
    Insert,

    /// <summary>No window had focus when dictation started, so there is no known target.</summary>
    NoTarget,

    /// <summary>Another window, or none, has focus now.</summary>
    FocusChanged,

    /// <summary>The target window was closed.</summary>
    TargetClosed,

    /// <summary>
    /// The target runs at a higher integrity level (as administrator). Windows silently drops
    /// input sent to it from Naad (UIPI), so a paste would look sent and never arrive.
    /// </summary>
    TargetElevated,
}

/// <summary>
/// Identifies a window. The thread and process ids guard against a closed window's handle
/// being reused by a new window before the paste.
/// </summary>
internal readonly record struct WindowTarget(nint Handle, uint ThreadId, uint ProcessId)
{
    public static WindowTarget None { get; } = new(0, 0, 0);
}

/// <summary>
/// Decides whether text may go into the window the user dictated into. Never switches focus:
/// when the answer is no, the caller keeps the transcript and tells the user.
/// </summary>
internal static class FocusGuard
{
    private static readonly int? OwnIntegrity = IntegrityOf((uint)Environment.ProcessId);

    /// <summary>The window that has focus now. Call on the UI thread when dictation starts.</summary>
    public static WindowTarget Foreground() => Identify(User32.GetForegroundWindow());

    /// <summary>Checks the target against the window that has focus now.</summary>
    public static InsertionDecision Check(WindowTarget target)
    {
        var exists = target.Handle != 0 && User32.IsWindow(target.Handle) && Identify(target.Handle) == target;
        return Decide(target, Foreground(), exists, exists ? IntegrityOf(target.ProcessId) : null, OwnIntegrity);
    }

    /// <summary>
    /// The pure decision. An unknown integrity level (null) never blocks: Windows is then left
    /// to accept or drop the input, as before this check existed.
    /// </summary>
    public static InsertionDecision Decide(
        WindowTarget target, WindowTarget foreground, bool targetExists, int? targetIntegrity, int? ownIntegrity)
    {
        if (target.Handle == 0)
        {
            return InsertionDecision.NoTarget;
        }

        if (!targetExists)
        {
            return InsertionDecision.TargetClosed;
        }

        if (foreground != target)
        {
            return InsertionDecision.FocusChanged;
        }

        return targetIntegrity > ownIntegrity ? InsertionDecision.TargetElevated : InsertionDecision.Insert;
    }

    /// <summary>
    /// A process's integrity level as the last RID of its integrity SID: 0x2000 for a normal
    /// process, 0x3000 for one run as administrator. Null if it can't be read.
    /// </summary>
    public static int? IntegrityOf(uint processId)
    {
        var process = Kernel32.OpenProcess(Kernel32.ProcessQueryLimitedInformation, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            if (!Advapi32.OpenProcessToken(process, Advapi32.TokenQuery, out var token))
            {
                return null;
            }

            try
            {
                return ReadIntegrity(token);
            }
            finally
            {
                Kernel32.CloseHandle(token);
            }
        }
        finally
        {
            Kernel32.CloseHandle(process);
        }
    }

    private static WindowTarget Identify(nint window)
    {
        if (window == 0)
        {
            return WindowTarget.None;
        }

        var thread = User32.GetWindowThreadProcessId(window, out var process);
        return thread == 0 ? WindowTarget.None : new(window, thread, process);
    }

    private static int? ReadIntegrity(nint token)
    {
        // The first call reports the size the label needs.
        Advapi32.GetTokenInformation(token, Advapi32.TokenIntegrityLevel, 0, 0, out var size);
        if (size == 0)
        {
            return null;
        }

        var label = Marshal.AllocHGlobal((int)size);
        try
        {
            if (!Advapi32.GetTokenInformation(token, Advapi32.TokenIntegrityLevel, label, size, out _))
            {
                return null;
            }

            var sid = Marshal.ReadIntPtr(label);
            var count = Marshal.ReadByte(Advapi32.GetSidSubAuthorityCount(sid));
            return count == 0 ? null : Marshal.ReadInt32(Advapi32.GetSidSubAuthority(sid, (uint)count - 1));
        }
        finally
        {
            Marshal.FreeHGlobal(label);
        }
    }
}
