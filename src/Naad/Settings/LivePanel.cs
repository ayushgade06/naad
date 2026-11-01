using static Naad.Settings.FormLayout;

namespace Naad.Settings;

/// <summary>The settings' Live page: words shown while speaking. Create and use it on the UI thread.</summary>
internal sealed class LivePanel : TableLayoutPanel
{
    private readonly CheckBox _preview = new()
    {
        Text = "Show the words in the overlay while I speak",
        AutoSize = true,
    };

    /// <param name="current">The settings in use.</param>
    public LivePanel(AppSettings current)
    {
        AutoSize = true;
        ColumnCount = 2;
        Padding = new Padding(12);
        _preview.Checked = current.LivePreview;

        Row(this, "Preview", _preview);
        Row(this, "", Note("With Whisper on this PC and a graphics card: each phrase is transcribed while " +
            "you speak, so the text is ready sooner. It starts from the second dictation after Naad starts " +
            "or settings are saved, once the model is loaded, and does nothing on the processor alone."));
    }

    /// <summary><paramref name="chosen"/> with this page's choices applied.</summary>
    public AppSettings Apply(AppSettings chosen) => chosen with { LivePreview = _preview.Checked };
}
