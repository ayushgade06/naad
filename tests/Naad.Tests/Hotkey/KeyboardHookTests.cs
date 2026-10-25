using Naad.Hotkey;

namespace Naad.Tests.Hotkey;

// Only the pure input filter; installing the hook itself is covered by manual checks.
public sealed class KeyboardHookTests
{
    private const uint Injected = 0x10;
    private const uint LowerIlInjected = 0x02;

    [Theory]
    // A physical key.
    [InlineData(0u, 0ul, false)]
    // Naad's own Ctrl+V.
    [InlineData(Injected, (ulong)KeyboardHook.OwnInputMarker, true)]
    // Another program's synthetic input, e.g. a key remapper.
    [InlineData(Injected, 0ul, false)]
    [InlineData(Injected, 0x1234ul, false)]
    // The marker without the injected flag is not Naad's: only SendInput sets that flag.
    [InlineData(0u, (ulong)KeyboardHook.OwnInputMarker, false)]
    // Input from a lower-integrity process (a sandbox) is never trusted.
    [InlineData(Injected | LowerIlInjected, 0ul, true)]
    [InlineData(LowerIlInjected, 0ul, true)]
    public void ShouldIgnore_InputSource_SkipsOnlyOwnAndLowerIntegrityInput(uint flags, ulong extraInfo, bool expected)
    {
        Assert.Equal(expected, KeyboardHook.ShouldIgnore(flags, (nuint)extraInfo));
    }
}
