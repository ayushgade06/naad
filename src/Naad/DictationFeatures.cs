using Naad.Context;
using Naad.Dictation;
using Naad.History;
using Naad.Hotkey;
using Naad.Insertion;
using Naad.Settings;
using Naad.Tray;

namespace Naad;

/// <summary>
/// Connects the dictation's extras to the controller: Esc, the hands-free latch and Paste again
/// from the keyboard hook; Edit Mode and context reading; the history; and the tray's history
/// and dictionary items. Every handler runs on the UI thread.
/// </summary>
internal static class DictationFeatures
{
    // Long enough for Windows to hand focus back from the closing tray menu to the app before it.
    private static readonly TimeSpan MenuCloseDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Wires everything up and returns the history recorder, which the caller disposes.</summary>
    /// <param name="controller">The dictation.</param>
    /// <param name="hook">Its keyboard hook.</param>
    /// <param name="overlay">Its overlay.</param>
    /// <param name="tray">The tray icon.</param>
    /// <param name="clipboard">For Edit Mode's copy fallback and copied words.</param>
    /// <param name="settings">The settings in use (or waiting to apply).</param>
    /// <param name="languageModelReady">Whether a language model can run, for context and Edit Mode.</param>
    /// <param name="saveWords">Saves a new dictionary; returns what went wrong, or null.</param>
    public static HistoryRecorder Connect(
        DictationController controller,
        KeyboardHook hook,
        RecordingOverlay overlay,
        TrayIcon tray,
        WindowsClipboard clipboard,
        Func<AppSettings> settings,
        Func<bool> languageModelReady,
        Func<IReadOnlyList<string>, string?> saveWords)
    {
        // Esc only means "cancel" while a dictation runs; otherwise it belongs to the app.
        hook.LatchArmed = false;
        controller.StateChanged += state =>
        {
            hook.EscapeArmed = state != DictationState.Idle;
            hook.LatchArmed = state == DictationState.Recording && !settings().HandsFree;
        };
        hook.EscapePressed += controller.Escape;
        hook.Latched += controller.Latch;
        hook.PasteAgainPressed += () => _ = controller.PasteAgainAsync();
        hook.SetPasteAgainShortcut(settings().PasteAgainShortcut);
        overlay.UseTimer(settings().OverlayTimer);

        controller.MicrophoneOpened += overlay.ShowMicrophoneOpen;
        controller.CleaningUp += overlay.ShowCleaningUp;
        controller.EditFailed += needsSelection =>
        {
            overlay.ShowNotice(needsSelection ? "Select text first" : "Edit failed");
            tray.ShowEditFailed(needsSelection);
        };

        var reader = new ContextReader(
            () =>
            {
                var s = settings();
                var model = languageModelReady();
                return new(
                    ReadNearby: model && s.Context && s.Cleanup == Cleanup.CleanupLevel.Ai,
                    s.ContextExcludedApps,
                    model ? s.EditMode : EditMode.Off,
                    Screenshot: model && s.Context && s.Cleanup == Cleanup.CleanupLevel.Ai && s.ContextScreenshot
                        && s.LanguageModelProvider == LanguageModel.LanguageModelProvider.Cloud,
                    s.ScreenshotSize,
                    s.ScreenshotWholeDesktop);
            },
            clipboard);
        controller.UseContextReader(reader.ReadAsync);

        var history = new HistoryRecorder(
            new HistoryStore(HistoryStore.DefaultFolder, TimeProvider.System),
            () => settings().HistoryRetention,
            TimeProvider.System);
        controller.Finished += history.Record;
        tray.AddHistoryItems(
            () => history.Records
                .Where(r => r.Outcome != HistoryOutcome.Failed)
                .Select(r => r.Text)
                .Take(10)
                .ToList(),
            controller.CopyAsync,
            async () =>
            {
                await Task.Delay(MenuCloseDelay);

                // After a restart, the newest dictation in the history.
                if (!await controller.PasteAgainAsync()
                    && history.Records.FirstOrDefault(r => r.Outcome != HistoryOutcome.Failed) is { } latest)
                {
                    await controller.PasteAsync(latest.Text);
                }
            },
            () => _ = AddCopiedWordsAsync(clipboard, tray, settings, saveWords));
        return history;
    }

    /// <summary>
    /// Transcribes a kept recording again with the settings in use, updates its record and copies
    /// the new text; null if it failed. An edit is redone on the selection it had.
    /// </summary>
    public static async Task<string?> RetryAsync(
        HistoryRecord record,
        Transcription.ITranscriber? transcriber,
        Cleanup.ITranscriptCleanup? cleanup,
        HistoryRecorder history,
        DictationController controller)
    {
        if (transcriber is null || history.Store.Audio(record.Id) is not { } wav)
        {
            return null;
        }

        var heard = await transcriber.TranscribeAsync(wav, CancellationToken.None);
        if (heard.Failure is not null || heard.Text.Length == 0)
        {
            return null;
        }

        var context = new DictationContext(record.App, null, record.Selection, null);
        var text = cleanup is null
            ? heard.Text
            : (await cleanup.CleanAsync(heard.Text, context, CancellationToken.None)).Text;
        if (text.Length == 0)
        {
            return null;
        }

        await history.UpdateAsync(record with { Raw = heard.Text, Text = text, Outcome = HistoryOutcome.Kept });
        await controller.CopyAsync(text);
        return text;
    }

    // The clipboard's words, split on commas, semicolons and new lines, minus those already known.
    private static async Task AddCopiedWordsAsync(
        WindowsClipboard clipboard,
        TrayIcon tray,
        Func<AppSettings> settings,
        Func<IReadOnlyList<string>, string?> save)
    {
        var copied = (await clipboard.CaptureAsync())?.Text;
        var known = settings().Words;
        var fresh = AppSettings.SplitWords(copied).Where(w => !known.Contains(w,
            StringComparer.OrdinalIgnoreCase)).ToList();
        if (fresh.Count > 0 && save([.. known, .. fresh]) is not null)
        {
            fresh.Clear();
        }

        tray.ShowWordsAdded(fresh.Count);
    }
}
