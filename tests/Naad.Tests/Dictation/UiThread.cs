using System.Collections.Concurrent;

namespace Naad.Tests.Dictation;

/// <summary>
/// A single thread with a synchronization context, standing in for the WinForms UI thread: every
/// await in the controller resumes here, one callback at a time, as it does in the app.
/// </summary>
internal sealed class UiThread : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];
    private readonly Lock _gate = new();
    private bool _disposed;

    public UiThread()
    {
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
    }

    // Work posted after the test ended is dropped: the controller may still have a timer pending.
    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _queue.Add((d, state));
            }
        }
    }

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new NotSupportedException("The controller never sends synchronously.");

    /// <summary>Runs an action on this thread and waits for it.</summary>
    public Task RunAsync(Action action) => RunAsync(() =>
    {
        action();
        return Task.FromResult(true);
    });

    /// <summary>Runs an async function on this thread and waits for its result.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> function)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(
            async _ =>
            {
                try
                {
                    done.SetResult(await function());
                }
                catch (Exception e)
                {
                    done.SetException(e);
                }
            },
            null);
        return done.Task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _queue.CompleteAdding();
        }
    }
}
