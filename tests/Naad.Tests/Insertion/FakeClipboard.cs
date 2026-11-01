using System.Text;
using Naad.Insertion;

namespace Naad.Tests.Insertion;

/// <summary>An in-memory clipboard with a sequence number, for tests of the insertion policy.</summary>
internal sealed class FakeClipboard(ClipboardSnapshot contents) : IClipboard
{
    public ClipboardSnapshot Contents { get; private set; } = contents;

    public List<string> Calls { get; } = [];

    // Every text written, in order; the paste restores the clipboard right after.
    public List<string> Texts { get; } = [];

    public bool CaptureFails { get; set; }

    public bool SetFails { get; init; }

    public bool RestoreFails { get; init; }

    public Action? Captured { get; set; }

    public uint SequenceNumber { get; private set; } = 100;

    public Task<ClipboardSnapshot?> CaptureAsync()
    {
        Calls.Add("capture");
        Captured?.Invoke();
        return Task.FromResult(CaptureFails ? null : Contents);
    }

    public Task<uint?> SetTextAsync(string text)
    {
        Calls.Add("set");
        Texts.Add(text);
        if (SetFails)
        {
            // Like a real failure after EmptyClipboard: the user's data is already gone.
            Write(ClipboardSnapshot.Empty);
            return Task.FromResult<uint?>(null);
        }

        Write(TextSnapshot(text));
        return Task.FromResult<uint?>(SequenceNumber);
    }

    public Task<bool> RestoreAsync(ClipboardSnapshot snapshot)
    {
        Calls.Add("restore");
        if (RestoreFails)
        {
            return Task.FromResult(false);
        }

        Write(snapshot);
        return Task.FromResult(true);
    }

    public void Write(ClipboardSnapshot snapshot)
    {
        Contents = snapshot;
        SequenceNumber++;
    }

    public static ClipboardSnapshot TextSnapshot(string text) =>
        new([new ClipboardFormatData(13, Encoding.Unicode.GetBytes(text + "\0"))], 0);

    public static string ReadText(ClipboardSnapshot snapshot) =>
        Encoding.Unicode.GetString(Assert.Single(snapshot.Formats).Data).TrimEnd('\0');
}
