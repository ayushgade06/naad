namespace Naad.Context;

/// <summary>When a dictation over selected text is an instruction to rewrite it.</summary>
internal enum EditMode
{
    /// <summary>Never: dictation over a selection replaces it, as typing would.</summary>
    Off,

    /// <summary>Whenever text is selected as the dictation starts.</summary>
    Automatic,

    /// <summary>
    /// Only when the Edit Mode key (Shift by default) is held as the shortcut is pressed, so plain
    /// dictation still replaces a selection.
    /// </summary>
    Manual,
}

/// <summary>
/// The key held with the shortcut for a Manual edit, on either side of the keyboard. Not Alt: let
/// go of on its own around a shortcut whose keys apps never see, it would open the app's menu.
/// </summary>
internal enum EditModeKey
{
    Shift,
    Ctrl,
}

/// <summary>How an edit was asked for, kept in its history record.</summary>
internal enum EditInvocation
{
    /// <summary>Text was selected as the dictation started.</summary>
    Automatic,

    /// <summary>The Edit Mode key was held with the shortcut.</summary>
    Manual,
}

internal enum EditIntent
{
    /// <summary>Dictate: the words are pasted.</summary>
    Dictate,

    /// <summary>Read the selection; if there is one, the words are an instruction for it.</summary>
    EditIfSelected,

    /// <summary>Manual mode asked for an edit: a selection is required.</summary>
    EditRequired,
}

/// <summary>Whether a dictation should read the selection, from the mode and the keys held. Pure.</summary>
internal static class EditModeDecision
{
    /// <param name="mode">The Edit Mode setting.</param>
    /// <param name="keyHeld">Whether the Edit Mode key is held.</param>
    public static EditIntent Decide(EditMode mode, bool keyHeld) => mode switch
    {
        EditMode.Automatic => EditIntent.EditIfSelected,
        EditMode.Manual when keyHeld => EditIntent.EditRequired,
        _ => EditIntent.Dictate,
    };
}
