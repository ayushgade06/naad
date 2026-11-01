namespace Naad.Context;

/// <summary>When a dictation over selected text is an instruction to rewrite it.</summary>
internal enum EditMode
{
    /// <summary>Never: dictation over a selection replaces it, as typing would.</summary>
    Off,

    /// <summary>Whenever text is selected as the dictation starts.</summary>
    Automatic,

    /// <summary>Only when Shift is held with the shortcut, so plain dictation still replaces a selection.</summary>
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
    public static EditIntent Decide(EditMode mode, bool shiftHeld) => mode switch
    {
        EditMode.Automatic => EditIntent.EditIfSelected,
        EditMode.Manual when shiftHeld => EditIntent.EditRequired,
        _ => EditIntent.Dictate,
    };
}
