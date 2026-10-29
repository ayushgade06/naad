using Naad.Hotkey;
using Key = System.Windows.Forms.Keys;

namespace Naad.Tests.Hotkey;

// Steps are written as "+Key" for key-down and "-Key" for key-up, using Keys enum names, since
// the capture sees sided modifiers (RControlKey) exactly as the hook reports them. The result
// is the last step's status, with the chord in its settings form when one was captured.
public sealed class ChordCaptureTests
{
    [Theory]
    // A modifier alone is a valid shortcut.
    [InlineData("+RControlKey -RControlKey", "Captured:RightCtrl")]
    // Sided modifiers stay sided.
    [InlineData("+LControlKey -LControlKey", "Captured:LeftCtrl")]
    [InlineData("+RMenu +RShiftKey -RShiftKey", "Captured:RightAlt+RightShift")]
    [InlineData("+LControlKey +Space -Space", "Captured:LeftCtrl+Space")]
    // Taken when the first key comes up, whichever it is.
    [InlineData("+LControlKey +Space -LControlKey", "Captured:LeftCtrl+Space")]
    // Up to four keys, in the order pressed.
    [InlineData("+LControlKey +LMenu +LShiftKey +F9 -F9", "Captured:LeftCtrl+LeftAlt+LeftShift+F9")]
    // A fifth key is ignored.
    [InlineData("+A +B +C +D +E -A", "Captured:A+B+C+D")]
    [InlineData("+A +B +C +D +E -E", "Capturing")]
    // Auto-repeat changes nothing.
    [InlineData("+RControlKey +RControlKey +RControlKey -RControlKey", "Captured:RightCtrl")]
    // Holding keys is not enough: nothing is taken until one comes up.
    [InlineData("+LControlKey +Space", "Capturing")]
    public void OnKey_KeysPressedThenReleased_CapturesThem(string steps, string expected)
    {
        Assert.Equal(expected, Run(steps));
    }

    [Theory]
    [InlineData("+Escape")]
    [InlineData("+LControlKey +Escape")]
    public void OnKey_Escape_Cancels(string steps)
    {
        Assert.Equal("Cancelled", Run(steps));
    }

    [Fact]
    public void OnKey_KeyHeldBeforeCapture_IsIgnoredWhenReleased()
    {
        // Right Ctrl was down when capture started, so only its key-up is seen.
        Assert.Equal("Capturing", Run("-RControlKey"));
        Assert.Equal("Captured:Space", Run("-RControlKey +Space -Space"));
    }

    [Fact]
    public void OnKey_KeyNoChordCanHold_IsRejected()
    {
        // 0xFF is past the last virtual-key code (VK_OEM_CLEAR, 0xFE); some drivers send it anyway.
        const Key Unassigned = (Key)0xFF;
        var capture = new ChordCapture();
        capture.OnKey(Unassigned, down: true);

        Assert.Equal(new CaptureStep(CaptureStatus.Rejected), capture.OnKey(Unassigned, down: false));
    }

    private static string Run(string steps)
    {
        var capture = new ChordCapture();
        var step = new CaptureStep(CaptureStatus.Capturing);
        foreach (var token in steps.Split(' '))
        {
            step = capture.OnKey(Enum.Parse<Key>(token[1..]), down: token[0] == '+');
        }

        return step.Chord is { } chord ? $"{step.Status}:{chord}" : step.Status.ToString();
    }
}
