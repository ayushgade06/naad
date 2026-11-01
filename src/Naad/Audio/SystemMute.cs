using System.Text.Json;

namespace Naad.Audio;

/// <summary>The default speakers' state, as <see cref="IOutputDevice"/> reads it.</summary>
/// <param name="Id">Windows' endpoint id; not user content.</param>
/// <param name="Muted">Whether the device is muted.</param>
/// <param name="Volume">Its master volume, 0 to 1.</param>
internal readonly record struct OutputState(string Id, bool Muted, float Volume);

/// <summary>The PC's speakers. Tests replace it; <see cref="WindowsOutput"/> is the real one.</summary>
internal interface IOutputDevice
{
    /// <summary>The default playback device, or null when there is none or it can't be read.</summary>
    OutputState? Default();

    /// <summary>The device with <paramref name="id"/>, or null when it is gone.</summary>
    OutputState? Find(string id);

    /// <summary>Mutes or unmutes it; false when Windows refused.</summary>
    bool SetMuted(string id, bool muted);
}

/// <summary>
/// Mutes the speakers while a dictation records, and puts back only what it changed: speakers
/// already muted are left alone, and ones the user unmuted meanwhile aren't touched again. The
/// device it muted is written to disk first, so a crash mid-dictation is undone at the next start.
/// Use on the UI thread.
/// </summary>
/// <param name="output">The speakers.</param>
/// <param name="path">Where the muted device is remembered.</param>
internal sealed class SystemMute(IOutputDevice output, string path)
{
    // Below a tenth of the volume, Naad's tones are hard to hear.
    private const float QuietVolume = 0.10f;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "muted-output.json");

    /// <summary>Whether a sound played now would barely be heard: muted, or the volume very low.</summary>
    public static bool LooksSilent(OutputState? state) => state is { } s && (s.Muted || s.Volume < QuietVolume);

    /// <summary>Mutes the default speakers, unless they already are or Naad muted them already.</summary>
    public void Mute()
    {
        if (Remembered() is not null || output.Default() is not { Muted: false } speakers)
        {
            return;
        }

        // Written before muting: a crash in between leaves a note with nothing to undo, never the
        // other way round.
        if (!Remember(speakers.Id))
        {
            return;
        }

        if (output.SetMuted(speakers.Id, muted: true))
        {
            Log.Info(LogEvent.OutputMuted);
        }
        else
        {
            Forget();
        }
    }

    /// <summary>Unmutes the speakers Naad muted, if they still are. Harmless when it muted none.</summary>
    public void Restore()
    {
        if (Remembered() is not { } id)
        {
            return;
        }

        // A device that is gone for now (a headset unplugged) keeps the note, to be put right later.
        switch (output.Find(id))
        {
            case null:
                return;
            case { Muted: true } when !output.SetMuted(id, muted: false):
                return;
            case { Muted: true }:
                Log.Info(LogEvent.OutputRestored);
                break;
        }

        Forget();
    }

    private string? Remembered()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Note>(File.ReadAllBytes(path))?.DeviceId : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error(LogEvent.OutputMuteFileFailed, e);
            return null;
        }
    }

    private bool Remember(string id)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(new Note(id)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Without the note a crash couldn't be undone, so nothing is muted.
            Log.Error(LogEvent.OutputMuteFileFailed, e);
            return false;
        }
    }

    private void Forget()
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(LogEvent.OutputMuteFileFailed, e);
        }
    }

    private sealed record Note(string DeviceId);
}
