namespace Naad.Tests;

public sealed class SingleInstanceGuardTests
{
    // A fresh name per test so tests never collide with each other or a running Naad.
    private static string UniqueName() => $@"Local\Naad.Tests.{Guid.NewGuid():N}";

    // Mutexes are re-entrant for their owning thread, so a competing instance has to be
    // simulated from a different thread.
    private static T OnOtherThread<T>(Func<T> action)
    {
        T result = default!;
        var thread = new Thread(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }

    [Fact]
    public void TryAcquire_NoOtherInstance_ReturnsGuard()
    {
        using var guard = SingleInstanceGuard.TryAcquire(UniqueName());

        Assert.NotNull(guard);
    }

    [Fact]
    public void TryAcquire_AnotherInstanceHoldsIt_ReturnsNull()
    {
        var name = UniqueName();
        using var first = SingleInstanceGuard.TryAcquire(name);

        var acquiredElsewhere = OnOtherThread(() =>
        {
            using var second = SingleInstanceGuard.TryAcquire(name);
            return second is not null;
        });

        Assert.NotNull(first);
        Assert.False(acquiredElsewhere);
    }

    [Fact]
    public void Dispose_OwnerReleases_NextInstanceCanAcquire()
    {
        var name = UniqueName();
        SingleInstanceGuard.TryAcquire(name)!.Dispose();

        var acquiredElsewhere = OnOtherThread(() =>
        {
            using var next = SingleInstanceGuard.TryAcquire(name);
            return next is not null;
        });

        Assert.True(acquiredElsewhere);
    }

    [Fact]
    public void TryAcquire_PreviousOwnerExitedWithoutReleasing_ReturnsGuard()
    {
        var name = UniqueName();

        // Simulates a crashed instance: the owning thread ends while still holding the
        // mutex. The handle stays open so the mutex survives and is marked abandoned.
        using var crashed = new Mutex(initiallyOwned: false, name);
        OnOtherThread(() => crashed.WaitOne(TimeSpan.Zero));

        // Join returns when the managed thread is done, slightly before Windows finishes ending
        // the OS thread and marks the mutex abandoned; until then it still looks owned. A real
        // crash ends the whole process first, so only the test has to wait.
        SingleInstanceGuard? guard = null;
        for (var attempt = 0; attempt < 100 && guard is null; attempt++)
        {
            guard = SingleInstanceGuard.TryAcquire(name);
            if (guard is null)
            {
                Thread.Sleep(10);
            }
        }

        using (guard)
        {
            Assert.NotNull(guard);
        }
    }
}
