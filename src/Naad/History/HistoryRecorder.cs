using System.Diagnostics;
using Naad.Dictation;

namespace Naad.History;

/// <summary>
/// Keeps finished dictations in the <see cref="HistoryStore"/> while the retention setting allows,
/// and deletes what it no longer keeps: at start, once a day, and when the setting changes. Disk
/// work runs off the UI thread. Never logs what a dictation said.
/// </summary>
internal sealed class HistoryRecorder : IDisposable
{
    // Retention counts in days; checking once a day keeps nothing much longer than promised.
    private static readonly TimeSpan EnforceEvery = TimeSpan.FromDays(1);

    private readonly HistoryStore _store;
    private readonly Func<HistoryRetention> _retention;
    private readonly TimeProvider _time;
    private readonly ITimer _daily;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly string _statsPath;
    private readonly Lock _statsGate = new();
    private LifetimeStats? _stats;
    private volatile IReadOnlyList<HistoryRecord> _records = [];

    /// <param name="store">Where the history is kept.</param>
    /// <param name="retention">The setting in use, read at each dictation.</param>
    /// <param name="time">Stamps records and runs the daily check.</param>
    /// <param name="statsPath">The lifetime counts behind the Home page's stats.</param>
    public HistoryRecorder(HistoryStore store, Func<HistoryRetention> retention, TimeProvider time, string statsPath)
    {
        _store = store;
        _statsPath = statsPath;
        _retention = retention;
        _time = time;
        _daily = time.CreateTimer(_ => Enforce(), null, TimeSpan.Zero, EnforceEvery);

        // Read (or seeded from the whole history, decrypted) off the UI thread, before it is shown.
        _ = Task.Run(() =>
        {
            lock (_statsGate)
            {
                LoadStats();
            }
        });
    }

    /// <summary>The history changed. Raised on the UI thread.</summary>
    public event Action? Changed;

    public HistoryStore Store => _store;

    public static string DefaultStatsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "stats.json");

    /// <summary>
    /// The lifetime stats as of <paramref name="today"/>; zeros while they are still being read. Never
    /// waits on the disk, so the UI thread can ask.
    /// </summary>
    public Stats Stats(DateOnly today)
    {
        if (!_statsGate.TryEnter())
        {
            return LifetimeStats.Empty.Compute(today);
        }

        try
        {
            return (_stats ?? LifetimeStats.Empty).Compute(today);
        }
        finally
        {
            _statsGate.Exit();
        }
    }

    /// <summary>
    /// The records, newest first, as of the last change. Decrypted off the UI thread, so the
    /// window and the tray menu never wait on the disk.
    /// </summary>
    public IReadOnlyList<HistoryRecord> Records => _records;

    /// <summary>The newest <paramref name="count"/> texts that went in or were kept, newest first.</summary>
    public static IReadOnlyList<string> RecentTexts(IEnumerable<HistoryRecord> records, int count) =>
        [.. records.Where(r => r.Outcome is HistoryOutcome.Pasted or HistoryOutcome.Kept).Select(r => r.Text)
            .Take(count)];

    /// <summary>
    /// Counts a finished dictation in the lifetime stats, even with the history off, and stores it
    /// unless the history is off. Call on the UI thread.
    /// </summary>
    public void Record(DictationReport report)
    {
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _time.LocalTimeZone).DateTime);
        if (_retention() == HistoryRetention.Off)
        {
            _ = Task.Run(() => Count(day, report, stored: false));
            return;
        }

        var record = new HistoryRecord(
            Guid.NewGuid(),
            _time.GetUtcNow(),
            AppName(report.ProcessId),
            report.AudioMilliseconds,
            report.Raw,
            report.Text,
            report.TranscribeMilliseconds,
            report.CleanupMilliseconds,
            report.Outcome,
            HasAudio: false,
            report.Selection,
            report.Invocation,
            report.LiveFellBack,
            report.Summary,
            report.Prompt,
            report.Cleanup);
        _ = Task.Run(() =>
        {
            // Stored first: stats seeded from the history now already hold this dictation.
            var stored = _store.Add(record, report.Wav);
            Count(day, report, stored);
            if (stored)
            {
                RaiseChanged();
            }
        });
    }

    /// <summary>Replaces a record, as after a retry; the recording stays. A deleted one stays deleted.</summary>
    public Task UpdateAsync(HistoryRecord record) => Task.Run(() =>
    {
        if (_store.All().Any(r => r.Id == record.Id))
        {
            _store.Add(record, _store.Audio(record.Id));
            RaiseChanged();
        }
    });

    /// <summary>
    /// Deletes what <paramref name="retention"/> (by default the setting in use) no longer keeps.
    /// Safe from any thread.
    /// </summary>
    public void Enforce(HistoryRetention? retention = null)
    {
        var deleted = _store.Enforce(retention ?? _retention());
        if (deleted > 0)
        {
            Log.Info(LogEvent.HistoryEnforced, ("deleted", deleted));
        }

        RaiseChanged();
    }

    /// <summary>Deletes one record.</summary>
    public void Delete(Guid id) => _ = Task.Run(() =>
    {
        _store.Delete(id);
        RaiseChanged();
    });

    /// <summary>Deletes the whole history.</summary>
    public void DeleteAll() => _ = Task.Run(() =>
    {
        _store.DeleteAll();
        Log.Info(LogEvent.HistoryCleared);
        RaiseChanged();
    });

    public void Dispose() => _daily.Dispose();

    // Off the UI thread; the counts hold no text, so a plain file written whole is enough.
    private void Count(DateOnly day, DictationReport report, bool stored)
    {
        lock (_statsGate)
        {
            var seeding = _stats is null && !File.Exists(_statsPath);
            if (LoadStats() is not { } stats
                || (seeding && stored)
                || !stats.Add(day, report.Outcome, report.Selection is not null, report.Text, report.AudioMilliseconds))
            {
                return;
            }

            var temp = _statsPath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_statsPath)!);
                File.WriteAllText(temp, stats.ToJson());
                File.Move(temp, _statsPath, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(LogEvent.StatsWriteFailed, e);
            }
        }
    }

    // Under _statsGate. A missing file starts from the history kept so far; a damaged one starts
    // over. A file that can't be read just now (locked by a backup, say) gives null and is tried
    // again later, so its counts are never overwritten.
    private LifetimeStats? LoadStats()
    {
        if (_stats is not null)
        {
            return _stats;
        }

        try
        {
            if (!File.Exists(_statsPath))
            {
                return _stats = LifetimeStats.Seed(_store.All(), _time.LocalTimeZone);
            }

            if (LifetimeStats.FromJson(File.ReadAllText(_statsPath)) is not { } read)
            {
                Log.Warn(LogEvent.StatsUnreadable);
                read = LifetimeStats.Empty;
            }

            return _stats = read;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.StatsUnreadable, e);
            return null;
        }
    }

    private static string? AppName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // Off the UI thread: reloads the records, then tells the UI.
    private void RaiseChanged()
    {
        _records = _store.All();
        if (_ui is null)
        {
            Changed?.Invoke();
        }
        else
        {
            _ui.Post(_ => Changed?.Invoke(), null);
        }
    }
}
