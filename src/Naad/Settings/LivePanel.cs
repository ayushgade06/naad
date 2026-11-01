using Naad.Live;

using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>
/// The settings' Live page: words shown or typed while speaking, and where they are transcribed.
/// Create and use it on the UI thread.
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

    /// <param name="current">The settings in use.</param>
    public LivePanel(AppSettings current)
    {
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);
        _preview.Checked = current.LivePreview;
        _typing.Checked = current.LiveTyping;
        _service.Items.AddRange(["This PC (needs a graphics card)", "OpenAI realtime", "Deepgram streaming"]);
        _service.SelectedIndex = (int)current.LiveService;

        Row(this, "Preview", _preview);
        Row(this, "Typing", _typing);
        Row(this, "", Note("Typed words skip cleanup. While you hold a shortcut key such as Right Ctrl, " +
            "nothing is typed, so the text is cleaned up and pasted as usual when you let go; in hands-free " +
            "mode it types as you speak. If you switch windows or the words are revised, typing stops and " +
            "the full text is kept for Copy last transcript."));
        Row(this, "Transcribe live on", _service);
        Row(this, "", Note("This PC: each phrase is transcribed by Whisper while you speak, from the " +
            "second dictation after Naad starts or settings are saved, once the model is loaded; on the " +
            "processor alone it does nothing. OpenAI and Deepgram stream your voice as you speak, with the " +
            "service's key from the Dictation or AI tab. If streaming fails, the recording is transcribed " +
            "as usual."));
    }

    /// <summary><paramref name="chosen"/> with this page's choices applied.</summary>
    public AppSettings Apply(AppSettings chosen) => chosen with
    {
        LivePreview = _preview.Checked,
        LiveTyping = _typing.Checked,
        LiveService = (LiveService)_service.SelectedIndex,

        // A model name belongs to one service; another would refuse it.
        LiveModel = (LiveService)_service.SelectedIndex == chosen.LiveService ? chosen.LiveModel : null,
    };
}
