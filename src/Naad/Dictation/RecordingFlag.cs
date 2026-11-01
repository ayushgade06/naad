namespace Naad.Dictation;

/// <summary>
/// The optional %LOCALAPPDATA%\Naad\is-recording file, for other tools (a stream overlay, a
/// script that mutes music) to poll: it exists only while a dictation records, and holds the
/// Unix time recording started, so a file left by a crash can be told from a fresh one. Holds
/// nothing else. Failures are logged and ignored: dictation never depends on it.
/// </summary>
internal static class RecordingFlag
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "is-recording");

    /// <summary>Creates the file while <paramref name="recording"/>, and removes it otherwise.</summary>
    public static void Update(bool recording, string path, DateTimeOffset now)
    {
        try
        {
            if (recording)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(
                    path, now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.RecordingFlagFailed, e);
        }
    }
}
