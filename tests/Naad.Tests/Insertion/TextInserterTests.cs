using System.Text;
using Naad.Insertion;

namespace Naad.Tests.Insertion;

// Runs the restore policy against a fake clipboard; the real one is never touched.
public sealed class TextInserterTests
{
    private const string Transcript = "synthetic transcript";
    private static readonly ClipboardSnapshot Original = TextSnapshot("synthetic previous clipboard");

    [Fact]
    public async Task Paste_Success_PastesOnceAndRestoresOriginal()
    {
        var clipboard = new FakeClipboard(Original);
        var pastes = 0;
        var callbacks = 0;
        var inserter = Inserter(clipboard, sendPaste: () =>
        {
            Assert.Equal(Transcript, ReadText(clipboard.Contents));
            pastes++;
            return true;
        });

        var outcome = await inserter.PasteAsync(Transcript, () => callbacks++, CancellationToken.None);

        Assert.Equal(InsertionOutcome.Pasted, outcome);
        Assert.Equal(1, pastes);
        Assert.Equal(1, callbacks);
        Assert.Equal(Original, clipboard.Contents);
        Assert.Equal(["capture", "set", "restore"], clipboard.Calls);
    }

    [Fact]
    public async Task Paste_EmptyClipboard_RestoresItEmpty()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty);

        var outcome = await Inserter(clipboard).PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.Pasted, outcome);
        Assert.Same(ClipboardSnapshot.Empty, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_SnapshotSkippedFormats_ReportsHowMany()
    {
        var inserter = Inserter(new FakeClipboard(Original with { Skipped = 2 }));

        await inserter.PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(2, inserter.LastSkippedFormats);
    }

    [Fact]
    public async Task Paste_ClipboardCantBeRead_PastesNothingAndWritesNothing()
    {
        var clipboard = new FakeClipboard(Original) { CaptureFails = true };
        var pastes = 0;

        var outcome = await Inserter(clipboard, sendPaste: () => ++pastes > 0)
            .PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.ClipboardUnavailable, outcome);
        Assert.Equal(0, pastes);
        Assert.Equal(["capture"], clipboard.Calls);
        Assert.Equal(Original, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_WriteFailsAfterEmptying_PutsOriginalBackWithoutPasting()
    {
        var clipboard = new FakeClipboard(Original) { SetFails = true };
        var pastes = 0;

        var outcome = await Inserter(clipboard, sendPaste: () => ++pastes > 0)
            .PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.ClipboardUnavailable, outcome);
        Assert.Equal(0, pastes);
        Assert.Equal(Original, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_InputRejected_RestoresOriginalAndSkipsCallback()
    {
        var clipboard = new FakeClipboard(Original);
        var callbacks = 0;

        var outcome = await Inserter(clipboard, sendPaste: () => false)
            .PasteAsync(Transcript, () => callbacks++, CancellationToken.None);

        Assert.Equal(InsertionOutcome.PasteRejected, outcome);
        Assert.Equal(0, callbacks);
        Assert.Equal(Original, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_UserCopiesBeforeRestore_KeepsTheNewContent()
    {
        var clipboard = new FakeClipboard(Original);
        var copiedByUser = TextSnapshot("synthetic newer copy");
        var inserter = Inserter(clipboard, sendPaste: () =>
        {
            // The user copies something while the target is still pasting.
            clipboard.Write(copiedByUser);
            return true;
        });

        var outcome = await inserter.PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.PastedClipboardChanged, outcome);
        Assert.Equal(copiedByUser, clipboard.Contents);
        Assert.DoesNotContain("restore", clipboard.Calls);
    }

    [Fact]
    public async Task Paste_InputRejectedAfterUserCopied_KeepsTheNewContent()
    {
        var clipboard = new FakeClipboard(Original);
        var copiedByUser = TextSnapshot("synthetic newer copy");

        var outcome = await Inserter(clipboard, sendPaste: () =>
        {
            clipboard.Write(copiedByUser);
            return false;
        }).PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.PasteRejected, outcome);
        Assert.Equal(copiedByUser, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_RestoreFails_ReportsIt()
    {
        var clipboard = new FakeClipboard(Original) { RestoreFails = true };

        var outcome = await Inserter(clipboard).PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.PastedRestoreFailed, outcome);
    }

    [Fact]
    public async Task Paste_ModifierHeldPastTimeout_TouchesNothing()
    {
        var clipboard = new FakeClipboard(Original);
        var pastes = 0;
        var inserter = new TextInserter(clipboard, () => true, () => ++pastes > 0, TimeSpan.Zero);

        var outcome = await inserter.PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.ModifiersHeld, outcome);
        Assert.Equal(0, pastes);
        Assert.Empty(clipboard.Calls);
    }

    [Fact]
    public async Task Paste_ModifierReleasedAfterAFewPolls_WaitsThenPastes()
    {
        var clipboard = new FakeClipboard(Original);
        var polls = 0;
        var inserter = new TextInserter(clipboard, () => ++polls <= 3, () => true, TimeSpan.Zero);

        var outcome = await inserter.PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.Pasted, outcome);
        Assert.Equal(4, polls);
    }

    [Fact]
    public async Task Paste_AlreadyCancelled_TouchesNothing()
    {
        var clipboard = new FakeClipboard(Original);

        var outcome = await Inserter(clipboard).PasteAsync(Transcript, null, new CancellationToken(canceled: true));

        Assert.Equal(InsertionOutcome.Cancelled, outcome);
        Assert.Empty(clipboard.Calls);
    }

    [Fact]
    public async Task Paste_CancelledWhileModifierHeld_TouchesNothing()
    {
        var clipboard = new FakeClipboard(Original);
        using var cancel = new CancellationTokenSource();
        var inserter = new TextInserter(
            clipboard,
            anyModifierDown: () =>
            {
                cancel.Cancel();
                return true;
            },
            sendPaste: () => true,
            TimeSpan.Zero);

        var outcome = await inserter.PasteAsync(Transcript, null, cancel.Token);

        Assert.Equal(InsertionOutcome.Cancelled, outcome);
        Assert.Empty(clipboard.Calls);
    }

    [Fact]
    public async Task Paste_CancelledDuringRestoreDelay_RestoresAtOnce()
    {
        var clipboard = new FakeClipboard(Original);
        using var cancel = new CancellationTokenSource();
        var inserter = new TextInserter(
            clipboard,
            anyModifierDown: () => false,
            sendPaste: () =>
            {
                cancel.Cancel();
                return true;
            },
            restoreDelay: TimeSpan.FromHours(1));

        var outcome = await inserter.PasteAsync(Transcript, null, cancel.Token)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(InsertionOutcome.Pasted, outcome);
        Assert.Equal(Original, clipboard.Contents);
    }

    [Fact]
    public async Task Paste_WhileAnotherPasteRuns_IsBusyAndTheFirstStillFinishes()
    {
        var clipboard = new FakeClipboard(Original);
        var pastes = 0;
        var inserter = new TextInserter(clipboard, () => false, () => ++pastes > 0, TimeSpan.FromMilliseconds(200));

        var first = inserter.PasteAsync(Transcript, null, CancellationToken.None);
        var second = await inserter.PasteAsync(Transcript, null, CancellationToken.None);

        Assert.Equal(InsertionOutcome.Busy, second);
        Assert.Equal(InsertionOutcome.Pasted, await first);
        Assert.Equal(1, pastes);
        Assert.Equal(InsertionOutcome.Pasted, await inserter.PasteAsync(Transcript, null, CancellationToken.None));
    }

    [Fact]
    public async Task Copy_Writes_LeavesTranscriptOnClipboard()
    {
        var clipboard = new FakeClipboard(Original);

        Assert.True(await Inserter(clipboard).CopyAsync(Transcript));

        Assert.Equal(Transcript, ReadText(clipboard.Contents));
    }

    [Fact]
    public async Task Copy_WriteFails_ReturnsFalse()
    {
        var clipboard = new FakeClipboard(Original) { SetFails = true };

        Assert.False(await Inserter(clipboard).CopyAsync(Transcript));
    }

    private static TextInserter Inserter(FakeClipboard clipboard, Func<bool>? sendPaste = null) =>
        new(clipboard, anyModifierDown: () => false, sendPaste ?? (() => true), TimeSpan.Zero);

    private static string ReadText(ClipboardSnapshot snapshot) =>
        Encoding.Unicode.GetString(Assert.Single(snapshot.Formats).Data).TrimEnd(' ');

    private static ClipboardSnapshot TextSnapshot(string text) =>
        new([new ClipboardFormatData(13, Encoding.Unicode.GetBytes(text + "\0"))], 0);

    private sealed class FakeClipboard(ClipboardSnapshot contents) : IClipboard
    {
        public ClipboardSnapshot Contents { get; private set; } = contents;

        public List<string> Calls { get; } = [];

        public bool CaptureFails { get; init; }

        public bool SetFails { get; init; }

        public bool RestoreFails { get; init; }

        public uint SequenceNumber { get; private set; } = 100;

        public Task<ClipboardSnapshot?> CaptureAsync()
        {
            Calls.Add("capture");
            return Task.FromResult(CaptureFails ? null : Contents);
        }

        public Task<uint?> SetTextAsync(string text)
        {
            Calls.Add("set");
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
    }
}
