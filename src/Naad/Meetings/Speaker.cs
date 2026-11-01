namespace Naad.Meetings;

/// <summary>Who spoke, by the track it was heard on.</summary>
internal enum Speaker
{
    /// <summary>The microphone: the user.</summary>
    You,

    /// <summary>The PC's sound: everyone else on the call.</summary>
    Others,
}
