namespace Naad.Meetings;

/// <summary>What Naad does when a call app starts using the microphone.</summary>
internal enum MeetingDetection
{
    /// <summary>Offer meeting notes in a notification; nothing records until the user clicks it.</summary>
    Notify,

    /// <summary>Nothing.</summary>
    Off,
}
