using Naad.Context;
using Naad.Hotkey;
using Naad.Settings;

namespace Naad.Tests.Settings;

public sealed class ShortcutsTests
{
    private static readonly AppSettings Defaults = AppSettings.Defaults;

    [Fact]
    public void Problem_Defaults_IsNone() => Assert.Null(Shortcuts.Problem(Defaults));

    [Fact]
    public void Problem_BothDictationRolesOff_SaysOneMustStayOn() =>
        Assert.Contains(
            "one way to dictate stays on",
            Shortcuts.Problem(Defaults with { HoldShortcut = null, ToggleShortcut = null }),
            StringComparison.Ordinal);

    [Fact]
    public void Problem_ToggleOnly_IsNone() =>
        Assert.Null(Shortcuts.Problem(Defaults with { HoldShortcut = null, ToggleShortcut = Parse("F9") }));

    [Theory]
    [InlineData("F9", "F9", null, "Hold to talk and Toggle (hands-free) both use F9")]
    [InlineData("RightCtrl+V", null, "RightCtrl+V", "Hold to talk and Paste again both use Right Ctrl+V")]
    [InlineData(null, "F10", "F10", "Toggle (hands-free) and Paste again both use F10")]
    public void Problem_TwoRolesShareAChord_NamesBoth(string? hold, string? toggle, string? paste, string expected)
    {
        var s = Defaults with
        {
            HoldShortcut = Optional(hold),
            ToggleShortcut = Optional(toggle),
            PasteAgainShortcut = Optional(paste),
        };

        Assert.StartsWith(expected, Shortcuts.Problem(s), StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_LongerChordOverAShorterOne_IsAllowed() =>
        // The longest chord held wins, so Right Ctrl and Right Ctrl+V can live together.
        Assert.Null(Shortcuts.Problem(Defaults with { PasteAgainShortcut = Parse("RightCtrl+V") }));

    [Theory]
    [InlineData("A")]
    [InlineData("Space")]
    [InlineData("D5")]
    [InlineData("OemComma")]
    public void Problem_TypingKeyAlone_IsRefused(string key) =>
        Assert.Contains("would stop typing", Shortcuts.Problem(Defaults with { HoldShortcut = Parse(key) }),
            StringComparison.Ordinal);

    [Fact]
    public void Problem_PasteAgainOnlyModifiers_IsRefused() =>
        Assert.Contains(
            "needs a key besides",
            Shortcuts.Problem(Defaults with { PasteAgainShortcut = Parse("LeftCtrl") }),
            StringComparison.Ordinal);

    [Fact]
    public void Problem_FunctionKeyAlone_IsAllowed() =>
        Assert.Null(Shortcuts.Problem(Defaults with { HoldShortcut = Parse("F9") }));

    [Fact]
    public void Problem_ManualEditKeyInTheShortcut_NamesIt() =>
        Assert.Contains(
            "Edit Mode key (Shift) is part of Hold to talk",
            Shortcuts.Problem(
                Defaults with { HoldShortcut = Parse("RightCtrl+RightShift"), EditMode = EditMode.Manual }),
            StringComparison.Ordinal);

    [Fact]
    public void Problem_EditKeyInTheShortcutButAutomatic_IsNone() =>
        Assert.Null(Shortcuts.Problem(Defaults with { HoldShortcut = Parse("RightCtrl+RightShift") }));

    [Fact]
    public void Choose_CustomChord_IsRememberedForTheRole()
    {
        var (next, error) = Shortcuts.Choose(Defaults, ShortcutRole.Toggle, Parse("LeftCtrl+LeftAlt+D"));

        Assert.Null(error);
        Assert.Equal("LeftCtrl+LeftAlt+D", next!.ToggleShortcut?.ToString());
        Assert.Equal("LeftCtrl+LeftAlt+D", next.CustomToggleShortcut?.ToString());
        Assert.Null(next.CustomHoldShortcut);
    }

    [Fact]
    public void Choose_PresetAfterACustom_KeepsTheCustomToOfferAgain()
    {
        var s = Defaults with { CustomHoldShortcut = Parse("LeftCtrl+LeftAlt+D") };

        var (next, _) = Shortcuts.Choose(s, ShortcutRole.Hold, Parse("F9"));

        Assert.Equal("F9", next!.HoldShortcut?.ToString());
        Assert.Equal("LeftCtrl+LeftAlt+D", next.CustomHoldShortcut?.ToString());
    }

    [Fact]
    public void Choose_Clash_RefusesAndChangesNothing()
    {
        var s = Defaults with { HoldShortcut = Parse("F9") };

        var (next, error) = Shortcuts.Choose(s, ShortcutRole.PasteAgain, Parse("F9"));

        Assert.Null(next);
        Assert.NotNull(error);
    }

    [Fact]
    public void Choose_DisablingTheOnlyDictationRole_IsRefused() =>
        Assert.NotNull(Shortcuts.Choose(Defaults, ShortcutRole.Hold, null).Error);

    [Theory]
    [InlineData("RightCtrl", null, "RightCtrl", false)]
    [InlineData(null, "F9", "F9", true)]
    [InlineData("RightCtrl", "F9", "RightCtrl", false)]
    public void Primary_Roles_TellsTheHoldFirst(string? hold, string? toggle, string expected, bool handsFree)
    {
        var (shortcut, pressed) = Shortcuts.Primary(
            Defaults with { HoldShortcut = Optional(hold), ToggleShortcut = Optional(toggle) });

        Assert.Equal((expected, handsFree), (shortcut.ToString(), pressed));
    }

    [Fact]
    public void Presets_EveryRole_HasNoClashWithItsOwnDefaults() =>
        Assert.All(
            Shortcuts.Presets(ShortcutRole.Hold),
            p => Assert.Null(Shortcuts.Problem(Defaults with { HoldShortcut = p })));

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException(text);

    private static Chord? Optional(string? text) => text is null ? null : Parse(text);
}
