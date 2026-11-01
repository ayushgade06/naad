using Naad.Hotkey;

namespace Naad.Tests.Hotkey;

public sealed class ShortcutRouterTests
{
    private static readonly Chord Hold = Chord.RightCtrl;
    private static readonly Chord Toggle = Parse("RightCtrl+RightShift");
    private static readonly Chord Paste = Parse("RightAlt+V");

    [Theory]
    [InlineData("Pressed", "HoldPressed")]
    [InlineData("Released", "HoldReleased")]
    [InlineData("Cancelled", "HoldCancelled")]
    public void Route_HoldChord_ActsOnEveryEvent(string kind, string expected) =>
        Assert.Equal([Enum.Parse<ShortcutAction>(expected)], Route(Event(Enum.Parse<ChordEventKind>(kind), Hold)));

    [Fact]
    public void Route_TogglePressed_Toggles() =>
        Assert.Equal([ShortcutAction.TogglePressed], Route(Event(ChordEventKind.Pressed, Toggle)));

    [Fact]
    public void Route_ToggleReleased_DoesNothing() => Assert.Empty(Route(Event(ChordEventKind.Released, Toggle)));

    [Fact]
    public void Route_PasteAgain_ActsOnItsPressOnly()
    {
        Assert.Equal([ShortcutAction.PasteAgainPressed], Route(Event(ChordEventKind.Pressed, Paste)));
        Assert.Empty(Route(Event(ChordEventKind.Released, Paste), Event(ChordEventKind.Cancelled, Paste)));
    }

    [Fact]
    public void Route_ToggleKeysJoinTheHeldHold_LatchesInsteadOfCancelling() =>
        Assert.Equal(
            [ShortcutAction.Latched],
            Route(Event(ChordEventKind.Cancelled, Hold), Event(ChordEventKind.Pressed, Toggle)));

    [Fact]
    public void Route_PasteKeysJoinTheHeldHold_CancelsThenPastes() =>
        Assert.Equal(
            [ShortcutAction.HoldCancelled, ShortcutAction.PasteAgainPressed],
            Route(Event(ChordEventKind.Cancelled, Hold), Event(ChordEventKind.Pressed, Paste)));

    [Fact]
    public void Route_HoldOff_IgnoresItsChord() =>
        Assert.Empty(ShortcutRouter.Route([new(ChordEventKind.Pressed, Hold)], null, Toggle, Paste));

    [Theory]
    [InlineData("Escape", "Escape")]
    [InlineData("Latched", "Latched")]
    public void Route_EscapeAndSpaceLatch_PassThrough(string kind, string expected) =>
        Assert.Equal([Enum.Parse<ShortcutAction>(expected)], Route(Event(Enum.Parse<ChordEventKind>(kind), Hold)));

    private static IReadOnlyList<ShortcutAction> Route(params ChordEvent[] events) =>
        ShortcutRouter.Route(events, Hold, Toggle, Paste);

    private static ChordEvent Event(ChordEventKind kind, Chord chord) => new(kind, chord);

    private static Chord Parse(string text) =>
        Chord.TryParse(text, out var chord) ? chord : throw new ArgumentException(text);
}
