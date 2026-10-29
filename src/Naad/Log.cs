using System.Globalization;
using System.Text;

namespace Naad;

/// <summary>
/// Naad's diagnostic log. A line can only hold a <see cref="LogEvent"/>, numeric fields with
/// short identifier names, and an exception's type name, never its message. So transcripts,
/// clipboard text, window titles and keys cannot reach the file, even by mistake.
/// </summary>
internal static class Log
{
    // Roughly ten thousand lines: months of normal use, yet small enough to attach to a bug
    // report. One rotated backup caps the disk use at about twice this.
    public const long DefaultMaxBytes = 1_000_000;
    public const string FileName = "naad.log";
    public const string BackupFileName = "naad.1.log";

    private const string InvalidFieldName = "invalid_field";
    private const int MaxFieldNameLength = 32;

    private static readonly Lock Gate = new();
    private static string? _directory;
    private static long _maxBytes = DefaultMaxBytes;

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "logs");

    /// <summary>Starts writing to a file in <paramref name="directory"/>; until then lines go to the console only.</summary>
    public static void Configure(string directory, long maxBytes = DefaultMaxBytes)
    {
        Directory.CreateDirectory(directory);
        lock (Gate)
        {
            _directory = directory;
            _maxBytes = maxBytes;
        }
    }

    /// <summary>Stops writing to the file, as when file logging is turned off; lines go to the console only.</summary>
    public static void StopFileLogging()
    {
        lock (Gate)
        {
            _directory = null;
        }
    }

    public static void Info(LogEvent logEvent, params ReadOnlySpan<(string Name, long Value)> fields) =>
        Write("INFO", logEvent, null, fields);

    public static void Warn(LogEvent logEvent, params ReadOnlySpan<(string Name, long Value)> fields) =>
        Write("WARN", logEvent, null, fields);

    public static void Error(LogEvent logEvent, params ReadOnlySpan<(string Name, long Value)> fields) =>
        Write("ERROR", logEvent, null, fields);

    /// <summary>Logs the exception's type only: messages can contain paths, user text or keys.</summary>
    public static void Error(LogEvent logEvent, Exception error, params ReadOnlySpan<(string Name, long Value)> fields) =>
        Write("ERROR", logEvent, error.GetType().FullName, fields);

    internal static string Format(
        DateTimeOffset time,
        string level,
        LogEvent logEvent,
        string? exceptionType,
        ReadOnlySpan<(string Name, long Value)> fields)
    {
        var line = new StringBuilder()
            .Append(time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(' ').Append(level)
            .Append(' ').Append(logEvent);
        foreach (var (name, value) in fields)
        {
            line.Append(' ').Append(IsSafeFieldName(name) ? name : InvalidFieldName)
                .Append('=').Append(value.ToString(CultureInfo.InvariantCulture));
        }

        if (exceptionType is not null)
        {
            line.Append(" exception=").Append(exceptionType);
        }

        return line.ToString();
    }

    // Field names are identifiers like "held_ms". Anything else (spaces, capitals,
    // punctuation, long text) is treated as a mistake and replaced.
    internal static bool IsSafeFieldName(string name) =>
        name.Length is > 0 and <= MaxFieldNameLength
        && name[0] is >= 'a' and <= 'z'
        && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static void Write(
        string level,
        LogEvent logEvent,
        string? exceptionType,
        ReadOnlySpan<(string Name, long Value)> fields)
    {
        var line = Format(DateTimeOffset.UtcNow, level, logEvent, exceptionType, fields);

        // Shown in the terminal when Naad is started with dotnet run.
        Console.WriteLine(line);

        lock (Gate)
        {
            if (_directory is null)
            {
                return;
            }

            try
            {
                AppendWithRotation(_directory, line);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A full disk or a locked file must never take Naad down.
                Console.Error.WriteLine($"Log write failed: {e.GetType().Name}");
            }
        }
    }

    private static void AppendWithRotation(string directory, string line)
    {
        var path = Path.Combine(directory, FileName);
        var current = new FileInfo(path);
        if (current.Exists && current.Length >= _maxBytes)
        {
            File.Move(path, Path.Combine(directory, BackupFileName), overwrite: true);
        }

        File.AppendAllText(path, line + Environment.NewLine);
    }
}
