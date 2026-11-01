using Naad.Live;
using Naad.Providers;
using Naad.Transcription;

using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>
/// The settings' Live page: words shown or typed while speaking, where they are transcribed, the
/// streaming model and a connection test. Create and use it on the UI thread.
/// </summary>
internal sealed class LivePanel : TableLayoutPanel
{
    private readonly CheckBox _preview = new()
    {
        Text = "Show the words in the overlay while I speak",
        AutoSize = true,
    };

    private readonly CheckBox _typing = new()
    {
        Text = "Type the words into the app while I speak",
        AutoSize = true,
    };

    private readonly ComboBox _service = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = FieldWidth };
    private readonly Button _reset = new() { Text = "Reset", AutoSize = true };
    private readonly Button _test = new() { Text = "Test connection", AutoSize = true };
    private readonly Label _testResult = Note("");
    private readonly Label _noGraphicsCard = Note("");
    private readonly AppSettings _current;
    private readonly Func<CloudService, string?> _keyFor;
    private readonly CancellationToken _closing;

    /// <param name="current">The settings in use.</param>
    /// <param name="keyFor">A service's stored key, or null; used only for the test.</param>
    /// <param name="closing">Cancelled when the window closes.</param>
    public LivePanel(AppSettings current, Func<CloudService, string?> keyFor, CancellationToken closing)
    {
        _current = current;
        _keyFor = keyFor;
        _closing = closing;
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);
        _preview.Checked = current.LivePreview;
        _typing.Checked = current.LiveTyping;

        // In LiveService's order.
        _service.Items.AddRange(
        [
            "This PC (needs a graphics card)", "OpenAI realtime", "Deepgram streaming", "Gemini Live",
            "xAI streaming", "Custom (OpenAI-compatible realtime)",
        ]);
        _service.SelectedIndex = (int)current.LiveService;
        _model.Text = current.LiveModel ?? "";

        Row(this, "Preview", _preview);
        Row(this, "", _noGraphicsCard);
        Row(this, "Typing", _typing);
        Row(this, "", Note("Typed words skip cleanup. They are typed while you hold the shortcut too; if you " +
            "switch windows or the words are revised, typing stops and the full text is kept for Copy last " +
            "transcript."));
        Row(this, "Transcribe live on", _service);
        Row(this, "Live model", Flow(_model, _reset));
        Row(this, "", Flow(_test, _testResult));
        Row(this, "", Note("This PC: each phrase is transcribed by Whisper while you speak, once the model is " +
            "loaded on a graphics card. The cloud services stream your voice as you speak, with the service's key " +
            "from the Dictation or AI tab; Custom uses the custom base URL there, with or without a key. If " +
            "streaming fails, the recording is transcribed as usual and Naad says so."));

        _service.SelectedIndexChanged += (_, _) => ServiceChanged();
        _model.DropDown += (_, _) =>
        {
            _model.Items.Clear();
            _model.Items.AddRange([.. RealtimeProtocol.SuggestedModels(Chosen)]);
        };
        _reset.Click += (_, _) => _model.Text = "";
        _test.Click += (_, _) => _ = TestAsync();
        ShowGraphicsCard();
    }

    private LiveService Chosen => (LiveService)_service.SelectedIndex;

    /// <summary><paramref name="chosen"/> with this page's choices applied.</summary>
    public AppSettings Apply(AppSettings chosen) => chosen with
    {
        LivePreview = _preview.Checked,
        LiveTyping = _typing.Checked,
        LiveService = Chosen,
        LiveModel = Model(),
    };

    /// <summary>
    /// Why live preview can't work with these choices, or null when it can: on this PC it needs a
    /// graphics card, which is known once the model has loaded (null until then).
    /// </summary>
    public static string? PreviewBlocked(LiveService service, bool? onGraphicsCard) =>
        service == LiveService.Local && onGraphicsCard == false
            ? "Whisper here runs on the processor, which is too slow to transcribe while you speak. Choose a " +
                "cloud service below, or use a PC with a graphics card."
            : null;

    // Blank, or a model that can't be one, means the service's default.
    private string? Model() =>
        _model.Text.Trim() is { Length: > 0 } typed && AppSettings.IsValidModel(typed) ? typed : null;

    // A model name belongs to one service; another would refuse it.
    private void ServiceChanged()
    {
        _model.Text = "";
        _testResult.Text = "";
        ShowGraphicsCard();
    }

    private void ShowGraphicsCard()
    {
        var blocked = PreviewBlocked(Chosen, LocalWhisperTranscriber.LastOnGraphicsCard);
        _noGraphicsCard.Text = blocked ?? "";
        _preview.Enabled = blocked is null;
        _typing.Enabled = blocked is null;
        _test.Enabled = Chosen != LiveService.Local;
    }

    // Fire-and-forget from a click: anything not caught here would vanish silently.
    private async Task TestAsync()
    {
        var s = _current with { LiveService = Chosen, LiveModel = Model() };
        if (LiveSetup.Stream(s, _keyFor) is not { } stream)
        {
            _testResult.Text = Chosen == LiveService.Custom
                ? "Save a custom base URL on the Dictation tab first."
                : "Save this service's key on the Dictation or AI tab first.";
            return;
        }

        _test.Enabled = false;
        _testResult.Text = "Connecting...";
        try
        {
            using var session = new RealtimeTranscript(
                Chosen, stream.Endpoint, stream.Key, stream.Model, s.Language, NoUpload.Instance);
            _testResult.Text = await session.TestAsync(_closing) ?? "Connected: the service takes the stream.";
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // The window closed while the test ran.
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.LiveStreamFailed, e);
            _testResult.Text = "The test couldn't run.";
        }
        finally
        {
            if (!IsDisposed)
            {
                _test.Enabled = true;
            }
        }
    }

    // A test streams silence and never falls back to an upload.
    private sealed class NoUpload : ITranscriber
    {
        public static NoUpload Instance { get; } = new();

        public Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken) =>
            Task.FromResult(new TranscriptionResult(""));
    }
}
