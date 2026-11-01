namespace Naad.Live;

/// <summary>
/// The text a streaming server has sent so far, put in order, and whether the stream has ended
/// with nothing missing. Final words of each utterance are stable and safe to type; partial words
/// may still change and are only shown. Pure; one caller at a time. User content: never logged.
/// </summary>
internal sealed class RealtimeText
{
    // Deepgram doesn't name its utterances; the one being spoken has this name.
    private const string Speaking = "#speaking";

    // Utterances in the order the server took them; OpenAI names each, Deepgram's come in order.
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _final = [];
    private readonly Dictionary<string, string> _partial = [];
    private bool _finishSent;
    private bool _endAcknowledged;

    /// <summary>The server reported an error, or the stream broke before it ended.</summary>
    public bool Failed { get; private set; }

    /// <summary>Every utterance the server took has its final words.</summary>
    public bool Settled => _order.All(_final.ContainsKey) && _partial.Count == 0;

    /// <summary>
    /// The stream ended with nothing missing: the server answered the end of the audio (OpenAI
    /// committed the rest, or had nothing left; Deepgram sent its closing metadata), and every
    /// utterance is final. Until then a quiet server proves nothing: it may still be listening.
    /// </summary>
    // ponytail: an utterance the server commits by itself just after the end is sent counts as
    // the answer; if the stream's last words ever go missing, match the answer to the commit.
    public bool Complete => !Failed && _endAcknowledged && Settled;

    /// <summary>The final words, up to the first utterance still waiting for them.</summary>
    public string Stable => string.Join(" ", _order
        .TakeWhile(_final.ContainsKey)
        .Select(item => _final[item])
        .Where(text => text.Length > 0));

    /// <summary>Everything heard so far, including words that may still change.</summary>
    public string Preview => string.Join(" ", _order
        .Select(item => _final.TryGetValue(item, out var text) ? text : _partial.GetValueOrDefault(item, ""))
        .Concat(_partial.Where(p => !_order.Contains(p.Key)).Select(p => p.Value))
        .Where(text => text.Length > 0));

    /// <summary>The end of the audio was sent; the server's answer to it is awaited.</summary>
    public void FinishSent() => _finishSent = true;

    /// <summary>The connection closed or broke: a failure unless the stream had already ended.</summary>
    public void Closed()
    {
        if (!Complete)
        {
            Failed = true;
        }
    }

    /// <summary>Takes in one event; returns whether the stable text may have changed.</summary>
    public bool Apply(RealtimeEvent e)
    {
        var item = e.Item ?? (e.Kind == RealtimeEventKind.Final ? $"#{_order.Count}" : Speaking);
        switch (e.Kind)
        {
            case RealtimeEventKind.Committed:
                Add(item);
                _endAcknowledged |= _finishSent;
                return false;
            case RealtimeEventKind.Partial:
                _partial[item] = e.Item is null ? e.Text : _partial.GetValueOrDefault(item, "") + e.Text;
                return false;
            case RealtimeEventKind.Final:
                Add(item);
                _final[item] = e.Text;
                _partial.Remove(e.Item ?? Speaking);
                return true;
            // Gemini says its turn is complete at every pause too: only after the end does it count.
            case RealtimeEventKind.Done:
                _endAcknowledged |= _finishSent;
                return false;
            case RealtimeEventKind.NothingToCommit:
                _endAcknowledged = true;
                return false;
            case RealtimeEventKind.Error:
                Failed = true;
                return false;
            default:
                return false;
        }
    }

    private void Add(string item)
    {
        if (!_order.Contains(item))
        {
            _order.Add(item);
        }
    }
}
