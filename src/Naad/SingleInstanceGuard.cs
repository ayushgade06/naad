namespace Naad;

/// <summary>
/// Owns a named mutex for the lifetime of the app so only one instance registers the
/// keyboard hook and tray icon. Dispose it on the thread that acquired it: Windows only
/// lets the owning thread release a mutex.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    // "Local\" scopes the mutex to the current Windows session, matching the per-session
    // keyboard hook and tray icon. The GUID keeps the name unique to Naad and must never
    // change, or an old and a new build could run side by side.
    public const string MutexName = @"Local\Naad.SingleInstance.A5F2782B-067F-4CFB-8471-B110B09BBBEF";

    private readonly Mutex _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the guard if this is the first instance, or null if another one holds it.</summary>
    public static SingleInstanceGuard? TryAcquire(string name = MutexName)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner exited without releasing it (a crash); Windows hands
            // ownership to us, so this instance is the only one running.
            owned = true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        if (owned)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
