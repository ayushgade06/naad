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
    private volatile IReadOnlyList<HistoryRecord> _records = [];

    /// <param name="store">Where the history is kept.</param>
    /// <param name="retention">The setting in use, read at each dictation.</param>
    /// <param name="time">Stamps records and runs the daily check.</param>
    public HistoryRecorder(HistoryStore store, Func<HistoryRetention> retention, TimeProvider time)
    {
        _store = store;
        _retention = retention;
        _time = time;
        _daily = time.CreateTimer(_ => Enforce(), null, TimeSpan.Zero, EnforceEvery);
    }

    /// <summary>The history changed. Raised on the UI thread.</summary>
    public event Action? Changed;

    public HistoryStore Store => _store;

    /// <summary>
    /// The records, newest first, as of the last change. Decrypted off the UI thread, so the
    /// window and the tray menu never wait on the disk.
    /// </summary>
    public IReadOnlyList<HistoryRecord> Records => _records;

    /// <summary>The newest <paramref name="count"/> texts that went in or were kept, newest first.</summary>
    public static IReadOnlyList<string> RecentTexts(IEnumerable<HistoryRecord> records, int count) =>
        [.. records.Where(r => r.Outcome != HistoryOutcome.Failed).Select(r => r.Text).Take(count)];

    /// <summary>Stores a finished dictation, unless the history is off. Call on the UI thread.</summary>
    public void Record(DictationReport report)
    {
        if (_retention() == HistoryRetention.Off)
        {
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
            report.LiveFellBack);
        _ = Task.Run(() =>
        {
            if (_store.Add(record, report.Wav))
            {
                RaiseChanged();
            }
        });
    }

    /// <summary>Replaces a record, as after a retry; the recording stays.</summary>
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
