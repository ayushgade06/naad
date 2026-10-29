using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Naad.Dictation;
using Naad.Native;
using Naad.Tray;

namespace Naad.Tests.Tray;

public sealed class RecordingOverlayTests
{
    [Theory]
    [InlineData(0x0800_0000)] // WS_EX_NOACTIVATE: never activated, so the focus guard still pastes.
    [InlineData(0x0000_0008)] // WS_EX_TOPMOST: above the window being dictated into.
    [InlineData(0x0000_0080)] // WS_EX_TOOLWINDOW: not in Alt+Tab or the taskbar.
    [InlineData(0x0000_0020)] // WS_EX_TRANSPARENT: clicks pass through.
    public void ExtendedStyles_IncludeEachRequiredStyle(int style)
    {
        Assert.Equal(style, RecordingOverlay.ExtendedStyles & style);
    }

    // Shows the overlay on the real desktop for about a second, over whatever window is in
    // front, and checks that window keeps the foreground. It sends no input and opens no
    // microphone, but it needs a desktop, so it never runs in CI. Run it like
    // WindowsClipboardTests.
    [Fact(Explicit = true)]
    public void ShowState_OnRealDesktop_NeverTakesTheForeground()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var before = User32.GetForegroundWindow();
                using var overlay = new RecordingOverlay();

                overlay.ShowState(DictationState.Recording, before);
                for (var i = 0; i < 20; i++)
                {
                    overlay.ShowLevel(1000 * i, TimeSpan.FromMilliseconds(50));
                    Application.DoEvents();
                    Thread.Sleep(25);
                }

                overlay.ShowState(DictationState.Transcribing, before);
                Application.DoEvents();

                Assert.True(overlay.Visible);
                Assert.Equal(before, User32.GetForegroundWindow());
                Assert.NotEqual(overlay.Handle, User32.GetForegroundWindow());
                Assert.False(overlay.ContainsFocus);

                overlay.ShowState(DictationState.Idle, before);
                Assert.False(overlay.Visible);
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
