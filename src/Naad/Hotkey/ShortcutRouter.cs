namespace Naad.Hotkey;

/// <summary>What a chord event means for the shortcut roles.</summary>
internal enum ShortcutAction
{
    HoldPressed,
    HoldReleased,

    /// <summary>Another key or the mouse joined the held hold-to-talk shortcut.</summary>
    HoldCancelled,
    TogglePressed,

    /// <summary>Another key joined the toggle shortcut as it went down.</summary>
    ToggleCancelled,
    PasteAgainPressed,
    Escape,

    /// <summary>The recording held with hold to talk carries on hands-free.</summary>
    Latched,
}

/// <summary>
/// Turns the matcher's chord events into what each shortcut role does. Pure. Hold to talk acts on
/// press, release and cancel; the toggle and paste again act on their press only. Holding the hold
/// shortcut and then adding keys that make the toggle shortcut (Right Ctrl, then Right Ctrl+Right
/// Shift) latches the recording instead of cancelling it.
/// </summary>
internal static class ShortcutRouter
{
    /// <param name="events">The matcher's events for one key, in order.</param>
    /// <param name="hold">Hold to talk's chord, or null when off.</param>
    /// <param name="toggle">The toggle's chord, or null when off.</param>
    /// <param name="pasteAgain">Paste again's chord, or null when off.</param>
    public static IReadOnlyList<ShortcutAction> Route(
        IReadOnlyList<ChordEvent> events, Chord? hold, Chord? toggle, Chord? pasteAgain)
    {
        var actions = new List<ShortcutAction>(events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            if (e.Kind is ChordEventKind.Escape)
            {
                actions.Add(ShortcutAction.Escape);
                continue;
            }

            if (e.Kind is ChordEventKind.Latched)
            {
                actions.Add(ShortcutAction.Latched);
                continue;
            }

            if (e.Chord == hold)
            {
                if (e.Kind == ChordEventKind.Cancelled && i + 1 < events.Count
                    && events[i + 1] is { Kind: ChordEventKind.Pressed } next && next.Chord == toggle)
                {
                    actions.Add(ShortcutAction.Latched);
                    i++;
                    continue;
                }

                actions.Add(e.Kind switch
                {
                    ChordEventKind.Pressed => ShortcutAction.HoldPressed,
                    ChordEventKind.Released => ShortcutAction.HoldReleased,
                    _ => ShortcutAction.HoldCancelled,
                });
            }
            else if (e.Chord == toggle)
            {
                if (e.Kind != ChordEventKind.Released)
                {
                    actions.Add(e.Kind == ChordEventKind.Pressed
                        ? ShortcutAction.TogglePressed
                        : ShortcutAction.ToggleCancelled);
                }
            }
            else if (e.Chord == pasteAgain && e.Kind == ChordEventKind.Pressed)
            {
                actions.Add(ShortcutAction.PasteAgainPressed);
            }
        }

        return actions;
    }
}
