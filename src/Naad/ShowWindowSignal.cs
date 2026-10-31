namespace Naad;

/// <summary>
/// Lets a second Naad ask the running one to open its window: starting Naad again is how a user
/// reaches it when the tray icon is hidden. A named event in this Windows session carries no data.
/// </summary>
internal sealed class ShowWindowSignal : IDisposable
{
    // Same session scope and GUID as the single-instance mutex, so only the matching Naad hears it.
    public const string EventName = @"Local\Naad.ShowWindow.A5F2782B-067F-4CFB-8471-B110B09BBBEF";

    private readonly EventWaitHandle _event;
    private readonly RegisteredWaitHandle _wait;

    /// <param name="shown">Called on a thread-pool thread each time a second Naad asks.</param>
    /// <param name="name">Tests pass their own.</param>
    public ShowWindowSignal(Action shown, string name = EventName)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _wait = ThreadPool.RegisterWaitForSingleObject(
            _event, (_, _) => shown(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>From a second instance: asks the running Naad to open its window. Best effort.</summary>
    public static void Send(string name = EventName)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var running))
        {
            using (running)
            {
                running.Set();
            }
        }
    }

    public void Dispose()
    {
        // Waits briefly for a callback already running, so none runs after Naad has shut down. The
        // callback only posts a message, so a second is ample; the limit means a callback that
        // disposed this itself can't wait on itself forever.
        using var done = new ManualResetEvent(false);
        if (_wait.Unregister(done))
        {
            done.WaitOne(TimeSpan.FromSeconds(1));
        }

        _event.Dispose();
    }
}
