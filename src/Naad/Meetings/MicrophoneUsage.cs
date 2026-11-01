using Microsoft.Win32;

namespace Naad.Meetings;

/// <summary>
/// Which apps use the microphone now, from the privacy records Windows keeps for Settings >
/// Privacy > Microphone (the consent store Naad already reads for its own access). Only app
/// names and in-use flags are read; nothing is logged.
/// </summary>
internal static class MicrophoneUsage
{
    /// <summary>Every app in the records, with whether it has the microphone open.</summary>
    public static IReadOnlyList<MicrophoneUse> Read()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(Audio.AudioRecorder.ConsentKey);
            if (root is null)
            {
                return [];
            }

            var uses = new List<MicrophoneUse>();
            foreach (var name in root.GetSubKeyNames().Where(n => n != "NonPackaged"))
            {
                using var app = root.OpenSubKey(name);
                uses.Add(new(AppName(name, packaged: true), InUse(app)));
            }

            using var desktop = root.OpenSubKey("NonPackaged");
            foreach (var name in desktop?.GetSubKeyNames() ?? [])
            {
                using var app = desktop!.OpenSubKey(name);
                uses.Add(new(AppName(name, packaged: false), InUse(app)));
            }

            return uses;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn(LogEvent.MeetingDetectionUnreadable);
            return [];
        }
    }

    /// <summary>
    /// The short name of a record's key: a program's path with '#' for '\' (such as
    /// "C:#Program Files#Zoom#bin#Zoom.exe") becomes "zoom"; a Store app's package family (such
    /// as "MSTeams_8wekyb3d8bbwe" or "5319275A.WhatsAppDesktop_cv1g1gvanyjgm") becomes its name
    /// without the publisher: "msteams", "whatsappdesktop".
    /// </summary>
    public static string AppName(string key, bool packaged)
    {
        var name = packaged ? key.Split('_')[0] : key[(key.LastIndexOf('#') + 1)..];
        if (packaged)
        {
            name = name[(name.LastIndexOf('.') + 1)..];
        }
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.ToLowerInvariant();
    }

    /// <summary>
    /// In use while the record's last start is later than its last stop, or it has no stop yet;
    /// both are FILETIMEs, 0 for never.
    /// </summary>
    public static bool InUse(long lastStart, long lastStop) => lastStart > 0 && (lastStop == 0 || lastStart > lastStop);

    private static bool InUse(RegistryKey? app) =>
        app is not null
        && InUse(app.GetValue("LastUsedTimeStart") as long? ?? 0, app.GetValue("LastUsedTimeStop") as long? ?? 0);
}
