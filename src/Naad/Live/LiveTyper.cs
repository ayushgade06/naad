using Naad.Insertion;

namespace Naad.Live;

/// <summary>
/// Types a dictation into its window while the user speaks, one finished phrase at a time. Only
/// text the transcriber won't revise is typed, and only ever appended: if the transcriber changes
/// words already typed, or focus leaves the window, typing stops for the rest of the dictation
/// and the full text is kept instead, so no words are lost. Typing goes on while the dictation
/// shortcut is held, but waits while any other modifier is: an app could read the typed letters
/// as shortcuts. Use on the UI thread. The text is user content: it is typed, never logged.
/// </summary>
/// <param name="target">The window being dictated into.</param>
/// <param name="check">Whether that window may receive text now.</param>
/// <param name="keysBlockTyping">Whether a held key, other than the shortcut's own, must be let go first.</param>
/// <param name="type">Types text as Naad's own key events, which the keyboard hook skips, with the
/// shortcut's held Ctrl or Shift released first.</param>
internal sealed class LiveTyper(
    WindowTarget target,
    Func<WindowTarget, InsertionDecision> check,
    Func<bool> keysBlockTyping,
    Func<string, TypingResult> type)
{
    private string _stable = "";
    private bool _allowed;
    private bool _finished;

    /// <summary>What has been typed so far.</summary>
    public string Typed { get; private set; } = "";

    /// <summary>Typing stopped early: the words were revised, focus moved, or typing failed.</summary>
    public bool Stopped { get; private set; }

    /// <summary>
    /// Lets typing start, once the dictation is known not to be an edit (typing over a selection
    /// would replace it); or stops it for good when it is.
    /// </summary>
    public void Allow(bool allowed)
    {
        if (!allowed)
        {
            Stopped = true;
            return;
        }

        _allowed = true;
        TypePending();
    }

    /// <summary>The text that won't change any more, from the start of the dictation.</summary>
    public void Update(string stable)
    {
        // An update posted before the dictation finished may arrive after it.
        if (Stopped || _finished)
        {
            return;
        }

        if (!stable.StartsWith(Typed, StringComparison.Ordinal))
        {
            Stop(LogEvent.LiveTypingRevised);
            return;
        }

        _stable = stable;
        TypePending();
    }

    /// <summary>The dictation ends before anything was typed: nothing is typed from now on.</summary>
    public void StopIfNothingTyped()
    {
        if (Typed.Length == 0)
        {
            _finished = true;
            Stopped = true;
        }
    }

    // Types what is stable but not typed yet, if no other key is held and the window is the same.
    private void TypePending()
    {
        if (Stopped || !_allowed || _stable.Length == Typed.Length || keysBlockTyping())
        {
            return;
        }

        if (check(target) != InsertionDecision.Insert)
        {
            Stop(LogEvent.LiveTypingFocusChanged);
            return;
        }

        // Partial: some keys went in, and what the window holds is unknown.
        if (type(_stable[Typed.Length..]) != TypingResult.Typed)
        {
            Stop(LogEvent.LiveTypingFailed);
            return;
        }

        Typed = _stable;
    }

    /// <summary>
    /// Types the rest of <paramref name="final"/>, the whole dictation's text, once the keys are
    /// up. Returns false when it doesn't continue what was typed, or typing stopped or couldn't
    /// finish: the caller then keeps the full text for the user.
    /// </summary>
    public bool Finish(string final)
    {
        _finished = true;
        if (Stopped)
        {
            return false;
        }

        if (!final.StartsWith(Typed, StringComparison.Ordinal))
        {
            Stop(LogEvent.LiveTypingRevised);
            return false;
        }

        _allowed = true;
        _stable = final;
        TypePending();
        return !Stopped && Typed.Length == final.Length;
    }

    private void Stop(LogEvent reason)
    {
        Stopped = true;
        Log.Warn(reason, ("typed_chars", Typed.Length));
    }
}
