using Naad.Insertion;

namespace Naad.Tests.Insertion;

public sealed class FocusGuardTests
{
    // Integrity RIDs: medium is a normal process, high is one run as administrator.
    private const int Medium = 0x2000;
    private const int High = 0x3000;

    private static readonly WindowTarget Notepad = new(0x1001, 10, 100);
    private static readonly WindowTarget Browser = new(0x2002, 20, 200);

    [Fact]
    public void Decide_TargetStillFocused_Inserts() =>
        Assert.Equal(InsertionDecision.Insert, FocusGuard.Decide(Notepad, Notepad, true, Medium, Medium));

    [Fact]
    public void Decide_OtherWindowFocused_IsFocusChanged() =>
        Assert.Equal(InsertionDecision.FocusChanged, FocusGuard.Decide(Notepad, Browser, true, Medium, Medium));

    [Fact]
    public void Decide_NothingFocused_IsFocusChanged() =>
        Assert.Equal(
            InsertionDecision.FocusChanged, FocusGuard.Decide(Notepad, WindowTarget.None, true, Medium, Medium));

    [Fact]
    public void Decide_SameHandleReusedByAnotherProcess_IsFocusChanged()
    {
        // The target closed and Windows gave its handle to a new window in another process.
        var reused = Notepad with { ThreadId = 30, ProcessId = 300 };

        Assert.Equal(InsertionDecision.FocusChanged, FocusGuard.Decide(Notepad, reused, true, Medium, Medium));
    }

    [Fact]
    public void Decide_TargetClosed_IsTargetClosedEvenIfHandleLooksFocused() =>
        Assert.Equal(InsertionDecision.TargetClosed, FocusGuard.Decide(Notepad, Notepad, false, null, Medium));

    [Fact]
    public void Decide_NoWindowAtStart_IsNoTarget() =>
        Assert.Equal(
            InsertionDecision.NoTarget,
            FocusGuard.Decide(WindowTarget.None, WindowTarget.None, false, null, Medium));

    [Fact]
    public void Decide_ElevatedTarget_IsTargetElevated() =>
        Assert.Equal(InsertionDecision.TargetElevated, FocusGuard.Decide(Notepad, Notepad, true, High, Medium));

    [Theory]
    [InlineData(Medium, High)]
    [InlineData(High, High)]
    [InlineData(0x1000, Medium)]
    public void Decide_TargetAtOrBelowOwnIntegrity_Inserts(int target, int own) =>
        Assert.Equal(InsertionDecision.Insert, FocusGuard.Decide(Notepad, Notepad, true, target, own));

    [Theory]
    [InlineData(null, Medium)]
    [InlineData(High, null)]
    [InlineData(null, null)]
    public void Decide_UnknownIntegrity_DoesNotBlock(int? target, int? own) =>
        Assert.Equal(InsertionDecision.Insert, FocusGuard.Decide(Notepad, Notepad, true, target, own));

    [Fact]
    public void Decide_ElevatedButFocusChanged_ReportsFocusChangeFirst() =>
        Assert.Equal(InsertionDecision.FocusChanged, FocusGuard.Decide(Notepad, Browser, true, High, Medium));

    // Reads this test process's own token: a real Win32 call that changes nothing.
    [Fact]
    public void IntegrityOf_ThisProcess_IsReadable() =>
        Assert.InRange(FocusGuard.IntegrityOf((uint)Environment.ProcessId) ?? -1, 0x1000, 0x4000);

    [Fact]
    public void IntegrityOf_NoSuchProcess_IsNull() =>
        Assert.Null(FocusGuard.IntegrityOf(uint.MaxValue - 2));

    // Needs a desktop with a normal (not elevated) window in front, so it never runs in CI. It
    // only reads window and process ids; it sends no input. Run it like WindowsClipboardTests.
    [Fact(Explicit = true)]
    public void Check_CurrentForegroundWindow_Inserts()
    {
        var foreground = FocusGuard.Foreground();

        Assert.NotEqual(WindowTarget.None, foreground);
        Assert.Equal(InsertionDecision.Insert, FocusGuard.Check(foreground));
        Assert.Equal(InsertionDecision.TargetClosed, FocusGuard.Check(foreground with { ProcessId = 1 }));
    }
}
