namespace Naad.Live;

/// <summary>A dictation's live session, and its typer when live typing is on.</summary>
internal sealed record LiveDictation(ILiveSession Session, LiveTyper? Typer) : IDisposable
{
    public void Dispose() => Session.Dispose();
}
