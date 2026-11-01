using Naad.LanguageModel;

namespace Naad.Meetings;

/// <summary>
/// Writes a meeting's summary, decisions and action items with the language model, on this PC
/// by default. A long meeting is summarised in sections, and the sections' notes combined, so it
/// fits any model's context. The model is told to use only what the transcript says and only the
/// speakers You and Others. The transcript and summary are user content: never logged.
/// </summary>
/// <param name="model">The language model in use.</param>
internal sealed class MeetingSummarizer(ILanguageModel model)
{
    // About 1,500 tokens of transcript per section: inside the smallest local model's context
    // with room for the instructions and the reply.
    // ponytail: counts characters, which fits Latin-script text; Devanagari or CJK runs two to four
    // times more tokens a character and can overflow a 4,096-token context. Measure in tokens if
    // summaries of such meetings fail.
    public const int SectionChars = 6000;

    // Notes merged per combine request: three sections' notes and the reply fit the smallest
    // context, so a long meeting is combined in rounds.
    public const int NotesPerCombine = 3;

    // A summary of a section, or of the whole, is short by design.
    private const int SummaryTokens = 700;

    // How MeetingTranscript.ClipboardText starts each turn.
    private static readonly string[] Speakers = ["You: ", "Others: "];

    // Long enough for the larger model on the processor to read a section and answer.
    private static readonly TimeSpan PerRequest = TimeSpan.FromSeconds(180);

    public const string Instructions = """
        You summarise a meeting transcript. Each line starts with who spoke: "You" (the person
        taking notes) or "Others" (everyone else on the call). Write Markdown with exactly these
        three sections:

        ## Summary
        Two to five short bullet points.

        ## Decisions
        Bullet points, or "None." if nothing was decided.

        ## Action items
        Bullet points, each saying who does it (You or Others) if the transcript says so, or
        "None." if there are none.

        Rules: use only what the transcript says. Never invent people, names, numbers, dates,
        decisions or action items. Refer to speakers only as You or Others. Write in the
        transcript's language. Reply with the three sections and nothing else.
        """;

    public const string CombineInstructions = """
        You are given notes from consecutive parts of one meeting, each with Summary, Decisions
        and Action items sections. Merge them into one set of the same three sections, in Markdown,
        removing repeats. Use only what the notes say; never add anything. Refer to speakers only
        as You or Others. Keep "None." for a section that is empty in every part. Reply with the
        three sections and nothing else.
        """;

    /// <summary>
    /// The transcript split into sections at turn boundaries, each at most <see cref="SectionChars"/> long.
    /// </summary>
    public static IReadOnlyList<string> Sections(string transcript)
    {
        var sections = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in transcript.Split(Environment.NewLine))
        {
            // A single turn longer than a section is cut, rather than sent whole; each piece keeps
            // its speaker, so the model never sees words without one.
            var speaker = Speakers.FirstOrDefault(p => line.StartsWith(p, StringComparison.Ordinal)) ?? "";
            var rest = line;
            while (rest.Length > 0)
            {
                var length = Math.Min(rest.Length, SectionChars - speaker.Length);
                if (length < rest.Length && char.IsHighSurrogate(rest[length - 1]))
                {
                    length--;
                }

                var part = rest[..length];
                rest = rest[length..];
                if (rest.Length > 0 && !rest.StartsWith(speaker, StringComparison.Ordinal))
                {
                    rest = speaker + rest;
                }

                if (current.Length > 0 && current.Length + part.Length + 1 > SectionChars)
                {
                    sections.Add(current.ToString());
                    current.Clear();
                }

                current.Append(current.Length > 0 ? "\n" : "").Append(part);
            }
        }

        if (current.Length > 0)
        {
            sections.Add(current.ToString());
        }

        return sections;
    }

    /// <summary>The summary of <paramref name="meeting"/>, or null when the model failed.</summary>
    public async Task<string?> SummarizeAsync(MeetingRecord meeting, CancellationToken cancellationToken)
    {
        var sections = Sections(MeetingTranscript.ClipboardText(meeting.Segments));
        if (sections.Count == 0)
        {
            return null;
        }

        var notes = new List<string>();
        foreach (var section in sections)
        {
            if (await AskAsync(Instructions, section, cancellationToken) is not { } note)
            {
                return null;
            }

            notes.Add(note);
        }

        // In rounds of a few notes, so no request outgrows the model's context.
        while (notes.Count > 1)
        {
            var merged = new List<string>();
            foreach (var batch in notes.Chunk(NotesPerCombine))
            {
                if (batch.Length == 1)
                {
                    merged.Add(batch[0]);
                }
                else if (await AskAsync(CombineInstructions, string.Join("\n\n---\n\n", batch), cancellationToken)
                    is { } note)
                {
                    merged.Add(note);
                }
                else
                {
                    return null;
                }
            }

            notes = merged;
        }

        var summary = notes[0];
        Log.Info(LogEvent.MeetingSummarized, ("sections", sections.Count), ("ok", summary is null ? 0 : 1));
        return summary;
    }

    private async Task<string?> AskAsync(string instructions, string text, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(PerRequest);
        try
        {
            var reply = await model.CompleteAsync(new(instructions, text, SummaryTokens), timeout.Token);
            if (reply.Failure is { } failure)
            {
                Log.Warn(LogEvent.MeetingSummaryFailed, ("error", (int)failure.Error));
                return null;
            }

            return reply.Text.Trim() is { Length: > 0 } summary ? summary : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warn(LogEvent.MeetingSummaryFailed, ("error", -1));
            return null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A model replaced by a settings change, or a native failure: no summary this time.
            Log.Error(LogEvent.MeetingSummaryFailed, e);
            return null;
        }
    }
}
