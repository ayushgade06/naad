namespace Naad.Meetings;

/// <summary>One app's microphone use, as Windows' privacy records show it.</summary>
/// <param name="App">The app's short name: a program's file name without ".exe", or a Store
/// app's package name before its publisher id, in lowercase.</param>
/// <param name="InUse">Whether it has the microphone open now.</param>
internal readonly record struct MicrophoneUse(string App, bool InUse);
