namespace Naad.Tests;

public sealed class ShowWindowSignalTests
{
    [Fact]
    public async Task Send_RunningInstanceListens_IsHeard()
    {
        var name = $@"Local\Naad.Test.{Guid.NewGuid():N}";
        // Resumed elsewhere: resuming on the callback's thread would dispose the signal from its own callback.
        var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var signal = new ShowWindowSignal(() => heard.TrySetResult(), name);

        ShowWindowSignal.Send(name);

        await heard.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Send_NoInstanceListening_DoesNothing()
    {
        ShowWindowSignal.Send($@"Local\Naad.Test.{Guid.NewGuid():N}");
    }
}
