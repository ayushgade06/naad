using System.Security;
using Microsoft.Win32;

namespace Naad.Settings;

/// <summary>
/// Starts Naad when the user signs in, through the per-user Run key. Windows reads that key, so
/// it, not settings.json, is the only record of the choice, and it can't drift out of step.
/// </summary>
/// <param name="root">The registry hive; tests pass a throwaway key.</param>
/// <param name="runKeyPath">The Run key under <paramref name="root"/>.</param>
/// <param name="exePath">The Naad.exe to start.</param>
// ponytail: points at the running exe; the installer (P22) moves Naad, so it rewrites this entry
// when it installs, and this window rewrites it on the next save.
internal sealed class StartupEntry(RegistryKey root, string runKeyPath, string exePath)
{
    /// <summary>HKCU's Run key: per user, so no administrator rights are needed.</summary>
    public const string DefaultRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const string ValueName = "Naad";

    /// <summary>The command Windows runs: the path in quotes, since it may contain spaces.</summary>
    public string Command => $"\"{exePath}\"";

    public static StartupEntry ForThisApp() =>
        new(Registry.CurrentUser, DefaultRunKey, Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of Naad.exe is unknown."));

    /// <summary>
    /// Whether this Naad.exe starts at sign-in. An entry left by a copy that was moved or deleted
    /// doesn't count, so saving with the box ticked points it here again.
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = root.OpenSubKey(runKeyPath);
            return key?.GetValue(ValueName) as string == Command;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Adds or removes the entry. Returns null once done, or the error that prevented it.</summary>
    public Exception? Set(bool enabled)
    {
        try
        {
            using var key = root.CreateSubKey(runKeyPath);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
        {
            return e;
        }
    }
}
