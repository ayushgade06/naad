using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Naad.Settings;
using Naad.Transcription;

namespace Naad.Tests.Settings;

public sealed class SettingsWindowTests
{
    // The window is modeless, where a button's DialogResult doesn't close it: Cancel and Esc
    // used to leave it open, so a model download kept running after "closing" it.
    [Fact]
    public void Cancel_Clicked_ClosesTheWindow()
    {
        var closed = false;
        OnStaThread(() =>
        {
            using var window = Window(_ => null);
            window.FormClosed += (_, _) => closed = true;
            _ = window.Handle;

            // PerformClick ignores a button that was never shown; showing the window would take focus.
            using var clicker = new Clicker();
            clicker.ClickOn((Control)window.CancelButton!);
        });

        Assert.True(closed);
    }

    [Fact]
    public void Save_Unchanged_SavesBothPagesSettingsWithoutNewKeys()
    {
        SettingsChange? saved = null;
        var current = AppSettings.Defaults with { TranscriptionProvider = TranscriptionProvider.Cloud };
        OnStaThread(() =>
        {
            using var window = Window(change =>
            {
                saved = change;
                return null;
            }, current);
            _ = window.Handle;

            using var clicker = new Clicker();
            clicker.ClickOn((Control)window.AcceptButton!);
        });

        Assert.Equal(current, saved?.Settings);
        Assert.Empty(saved!.NewKeys);
        Assert.False(saved.StartAtLogin);
    }

    private static SettingsWindow Window(Func<SettingsChange, string?> save, AppSettings? current = null) => new(
        current ?? AppSettings.Defaults,
        microphones: [],
        startsAtLogin: false,
        new SettingsWindowActions(
            _ => new ApiKeyResult(null, ApiKeySource.None, SecretStatus.Missing),
            (_, _, _) => Task.FromResult(new ModelDownloadResult(ModelDownloadOutcome.Downloaded)),
            (_, _, _, _) => throw new InvalidOperationException("Not used."),
            (_, _, _) => throw new InvalidOperationException("Not used."),
            (_, _, _) => throw new InvalidOperationException("Not used."),
            (_, _, _) => throw new InvalidOperationException("Not used."),
            _ => null,
            save,
            () => false,
            () => { }));

    private sealed class Clicker : Control
    {
        public void ClickOn(Control control) => InvokeOnClick(control, EventArgs.Empty);
    }

    private static void OnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
