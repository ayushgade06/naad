using System.Diagnostics;
using Naad.LanguageModel;
using Naad.Meetings;

namespace Naad.Tests.Meetings;

[Collection("Log")]
public sealed class MeetingSummarizerTests
{
    [Fact]
    public void Sections_LongTranscript_SplitsAtTurnsWithinTheLimit()
    {
        var turn = "You: " + new string('a', 2500);
        var transcript = string.Join(Environment.NewLine, Enumerable.Repeat(turn, 5));

        var sections = MeetingSummarizer.Sections(transcript);

        Assert.Equal(3, sections.Count);
        Assert.All(sections, s => Assert.InRange(s.Length, 1, MeetingSummarizer.SectionChars));
        Assert.Equal(5, sections.Sum(s => s.Split('\n').Length));
    }

    [Fact]
    public void Sections_OneVeryLongTurn_IsCut()
    {
        var sections = MeetingSummarizer.Sections("Others: " + new string('b', 13000));

        Assert.Equal(3, sections.Count);
        Assert.All(sections, s => Assert.InRange(s.Length, 1, MeetingSummarizer.SectionChars));
    }

    [Fact]
    public async Task SummarizeAsync_ShortMeeting_AsksOnceWithTheRules()
    {
        var model = new FakeModel(_ => "## Summary\n- A synthetic point.");

        var summary = await new MeetingSummarizer(model).SummarizeAsync(Meeting(10), CancellationToken.None);

        Assert.Equal("## Summary\n- A synthetic point.", summary);
        var prompt = Assert.Single(model.Prompts);
        Assert.Contains("Never invent", prompt.Instructions, StringComparison.Ordinal);
        Assert.StartsWith("You: Synthetic words", prompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_LongMeeting_SummarisesSectionsThenCombines()
    {
        var model = new FakeModel(p => p.Instructions == MeetingSummarizer.CombineInstructions ? "combined" : "part");

        var summary = await new MeetingSummarizer(model).SummarizeAsync(Meeting(400), CancellationToken.None);

        Assert.Equal("combined", summary);
        Assert.True(model.Prompts.Count >= 3);
        Assert.StartsWith("part\n\n---\n\npart", model.Prompts[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Sections_CutTurn_KeepsItsSpeakerOnEveryPiece()
    {
        var sections = MeetingSummarizer.Sections("Others: " + new string('b', 13000));

        Assert.All(sections, s => Assert.StartsWith("Others: ", s, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SummarizeAsync_ManySections_CombinesInSmallRounds()
    {
        var model = new FakeModel(p => p.Instructions == MeetingSummarizer.CombineInstructions ? "combined" : "part");

        var summary = await new MeetingSummarizer(model).SummarizeAsync(Meeting(2000), CancellationToken.None);

        Assert.Equal("combined", summary);
        Assert.All(
            model.Prompts.Where(p => p.Instructions == MeetingSummarizer.CombineInstructions),
            p => Assert.InRange(p.Text.Split("---").Length, 2, MeetingSummarizer.NotesPerCombine));
    }

    [Fact]
    public async Task SummarizeAsync_ModelFails_ReturnsNull()
    {
        var model = new FakeModel(_ => null);

        Assert.Null(await new MeetingSummarizer(model).SummarizeAsync(Meeting(10), CancellationToken.None));
    }

    // Needs the local language model; never runs in CI. Run with:
    // tests/Naad.Tests/bin/Debug/net10.0-windows/Naad.Tests.exe
    // -class Naad.Tests.Meetings.MeetingSummarizerTests -explicit only -showLiveOutput
    // NAAD_TEST_LLM=Large uses the larger model. The meeting is synthetic and has no decisions or
    // action items, so a summary that lists any, or names a speaker, invented it.
    [Fact(Explicit = true)]
    public async Task SummarizeAsync_RealModel_InventsNoActionItemsOrSpeakers()
    {
        var output = TestContext.Current.TestOutputHelper!;
        var file = LanguageModelFiles.For(Environment.GetEnvironmentVariable("NAAD_TEST_LLM") == "Large"
            ? LocalLanguageModelChoice.Large
            : LocalLanguageModelChoice.Small);
        Assert.True(file.IsReady(), "Download the language model in Naad's settings first.");
        using var model = new LocalLanguageModel(file.Path, TimeProvider.System);
        MeetingSegment[] chat =
        [
            new(Speaker.You, 0, "Morning. Did the weekend go well?"),
            new(Speaker.Others, 4, "It did, thanks. We went hiking near the lake, the weather was lovely."),
            new(Speaker.You, 11, "Nice. I mostly read and cooked. The new bakery on the corner is good."),
            new(Speaker.Others, 18, "I keep meaning to try it. Anyway, I have to jump into another call now."),
            new(Speaker.You, 24, "Sure, talk later."),
        ];
        var meeting = new MeetingRecord(Guid.NewGuid(), "Chat", DateTimeOffset.Now, TimeSpan.FromSeconds(30), chat);

        var started = Stopwatch.GetTimestamp();
        var summary = await new MeetingSummarizer(model).SummarizeAsync(meeting, TestContext.Current.CancellationToken);
        output.WriteLine($"{Stopwatch.GetElapsedTime(started).TotalSeconds:F1} s");
        output.WriteLine(summary ?? "(no summary)");

        Assert.NotNull(summary);
        var actions = summary[(summary.IndexOf("## Action items", StringComparison.Ordinal) + 15)..];
        Assert.Contains("None", actions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Speaker", summary, StringComparison.OrdinalIgnoreCase);
    }

    // About 33 characters a segment: 400 segments make several sections.
    private static MeetingRecord Meeting(int segments) => new(
        Guid.NewGuid(),
        "Meeting",
        DateTimeOffset.Now,
        TimeSpan.FromMinutes(5),
        [.. Enumerable.Range(0, segments).Select(i => new MeetingSegment(
            i % 2 == 0 ? Speaker.You : Speaker.Others, i, $"Synthetic words number {i}."))]);

    private sealed class FakeModel(Func<ChatPrompt, string?> reply) : ILanguageModel
    {
        public List<ChatPrompt> Prompts { get; } = [];

        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(reply(prompt) is { } text
                ? new CompletionResult(text)
                : new CompletionResult("", new(CompletionError.ServerError, 500)));
        }
    }
}
