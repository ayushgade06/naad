using System.Drawing;
using Naad.Context;
using Naad.LanguageModel;

namespace Naad.Tests.Context;

// The context pieces that need no window: the prompt, failures, the screenshot's size and the
// excluded apps. Every text here is invented.
[Collection("Log")]
public sealed class ContextTests
{
    private static readonly DictationContext Read = new(
        "outlook", "Hi Priya, about the Q3 synthetic budget", null, null, WindowTitle: "Synthetic subject - Inbox");

    [Fact]
    public void Build_Metadata_SendsTheAppTitleAndNearbyText()
    {
        var prompt = ContextPrompt.Build(null, Read);

        Assert.Equal(ContextPrompt.DefaultInstructions, prompt.Instructions);
        Assert.Contains("App: outlook", prompt.Text, StringComparison.Ordinal);
        Assert.Contains("Window title: Synthetic subject - Inbox", prompt.Text, StringComparison.Ordinal);
        Assert.Contains("<<<\nHi Priya", prompt.Text, StringComparison.Ordinal);
        Assert.Null(prompt.Image);
    }

    [Fact]
    public void Build_TitleNotRead_SaysSo() =>
        Assert.Contains(
            "Window title: not read",
            ContextPrompt.Build(null, Read with { WindowTitle = null }).Text,
            StringComparison.Ordinal);

    [Fact]
    public void Build_Screenshot_GoesWithIt()
    {
        var prompt = ContextPrompt.Build("Custom.", Read with { Screenshot = [0xFF, 0xD8, 1] });

        Assert.Equal("Custom.", prompt.Instructions);
        Assert.Equal([0xFF, 0xD8, 1], prompt.Image);
        Assert.Contains("in this window", prompt.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("They write. They reply to Priya. They add a third.", "They write. They reply to Priya.")]
    [InlineData("One sentence only.", "One sentence only.")]
    [InlineData("No full stop", "No full stop")]
    [InlineData("Line one.\nLine two.", "Line one. Line two.")]
    [InlineData("   ", null)]
    public void TwoSentences_Reply_KeepsAtMostTwoOnOneLine(string reply, string? expected) =>
        Assert.Equal(expected, ContextPrompt.TwoSentences(reply));

    [Theory]
    [InlineData("Unauthorized", 401, "Key")]
    [InlineData("Rejected", 404, "ModelNotFound")]
    [InlineData("Rejected", 400, "Unusable")]
    [InlineData("RateLimited", 429, "RateLimited")]
    [InlineData("Timeout", 0, "Timeout")]
    [InlineData("Network", 0, "Network")]
    [InlineData("ServerError", 503, "Server")]
    [InlineData("MalformedResponse", 0, "Unusable")]
    public void From_Failure_NamesTheReason(string error, int code, string expected) =>
        Assert.Equal(
            Enum.Parse<ContextFailure>(expected),
            ContextFailures.From(new(Enum.Parse<CompletionError>(error), code)));

    [Fact]
    public void Message_EveryReason_IsDistinctAndFitsANotification()
    {
        var messages = Enum.GetValues<ContextFailure>().Select(ContextFailures.Message).ToList();

        Assert.Equal(messages.Count, messages.Distinct().Count());
        Assert.All(messages, m => Assert.True(m.Length <= 255, m));
    }

    [Theory]
    [InlineData("keepass", true)]
    [InlineData("KeePass", true)]
    [InlineData("notepad", false)]
    [InlineData(null, false)]
    public void IsExcluded_App_MatchesInAnyCaseWithOrWithoutExe(string? app, bool expected) =>
        Assert.Equal(expected, ContextReader.IsExcluded(app, ["KEEPASS.exe", "signal"]));

    [Theory]
    [InlineData(10, 5, 10)]
    [InlineData(5000, 0, 2000)]
    [InlineData(5000, 5000, 2000)]
    public void Around_LongText_KeepsTheLimitNearTheCaret(int length, int caret, int expected) =>
        Assert.Equal(expected, ContextReader.Around(new string('x', length), caret).Length);

    [Theory]
    [InlineData(0, "abcdef")]
    [InlineData(10, "efghij")]
    [InlineData(5, "cdefgh")]
    public void Around_Caret_KeepsTheTextAroundIt(int caret, string expected) =>
        Assert.Equal(expected, ContextReader.Around("abcdefghij", caret, limit: 6));

    [Fact]
    public void Jpeg_Window_IsAJpegNoWiderThanAsked()
    {
        using var shot = Noise(1600, 900);

        var jpeg = ScreenshotEncoder.Jpeg(shot, longestSide: 800)!;

        using var image = Image.FromStream(new MemoryStream(jpeg));
        Assert.Equal([0xFF, 0xD8], jpeg[..2]);
        Assert.True(image.Width <= 800);
        Assert.True(jpeg.Length <= ScreenshotEncoder.MaxBytes);
    }

    [Fact]
    public void Jpeg_OverTheCap_ShrinksUntilItFits()
    {
        using var shot = Noise(1024, 768);
        var largest = ScreenshotEncoder.Jpeg(shot, 1024, maxBytes: int.MaxValue)!;

        var capped = ScreenshotEncoder.Jpeg(shot, 1024, maxBytes: largest.Length / 2)!;

        Assert.True(capped.Length <= largest.Length / 2);
    }

    [Fact]
    public void Jpeg_CapTooSmall_IsNone()
    {
        using var shot = Noise(400, 300);

        Assert.Null(ScreenshotEncoder.Jpeg(shot, 400, maxBytes: 100));
    }

    [Fact]
    public async Task Summarize_Reply_KeepsTwoSentences()
    {
        var summary = await Summarizer(new("They write. They reply. More.")).SummarizeAsync(
            Task.FromResult(Read), CancellationToken.None);

        Assert.Equal(new ContextSummary("They write. They reply."), summary);
    }

    [Fact]
    public async Task Summarize_ModelFails_SaysWhyAndGivesNoText()
    {
        var summary = await Summarizer(new("", new(CompletionError.Unauthorized, 401))).SummarizeAsync(
            Task.FromResult(Read), CancellationToken.None);

        Assert.Equal(new ContextSummary(null, ContextFailure.Key), summary);
    }

    [Fact]
    public async Task Summarize_EmptyReply_IsUnusable() =>
        Assert.Equal(
            ContextFailure.Unusable,
            (await Summarizer(new("")).SummarizeAsync(Task.FromResult(Read), CancellationToken.None)).Failure);

    [Fact]
    public async Task Summarize_TooSlow_TimesOut()
    {
        var summarizer = new ContextSummarizer(new Hanging(), () => null, TimeSpan.FromMilliseconds(20));

        var summary = await summarizer.SummarizeAsync(Task.FromResult(Read), CancellationToken.None);

        Assert.Equal(ContextFailure.Timeout, summary.Failure);
    }

    [Fact]
    public async Task Summarize_NothingToGoOn_AsksNothing()
    {
        var model = new Fixed(new("unused."));
        var read = DictationContext.None;

        var summary = await new ContextSummarizer(model, () => null, TimeSpan.FromSeconds(1))
            .SummarizeAsync(Task.FromResult(read), CancellationToken.None);

        Assert.Equal(ContextSummary.None, summary);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task Summarize_ModelThrows_IsUnusable()
    {
        var summarizer = new ContextSummarizer(new Throwing(), () => null, TimeSpan.FromSeconds(1));

        var summary = await summarizer.SummarizeAsync(Task.FromResult(Read), CancellationToken.None);

        Assert.Equal(ContextFailure.Unusable, summary.Failure);
    }

    private static ContextSummarizer Summarizer(CompletionResult reply) =>
        new(new Fixed(reply), () => null, TimeSpan.FromSeconds(5));

    // Random pixels compress badly, like a busy window.
    private static Bitmap Noise(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        var random = new Random(7);
        for (var y = 0; y < height; y += 2)
        {
            for (var x = 0; x < width; x += 2)
            {
                bitmap.SetPixel(x, y, Color.FromArgb(random.Next(256), random.Next(256), random.Next(256)));
            }
        }

        return bitmap;
    }

    private sealed class Fixed(CompletionResult reply) : ILanguageModel
    {
        public int Calls { get; private set; }

        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(reply);
        }
    }

    private sealed class Throwing : ILanguageModel
    {
        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken) =>
            throw new ObjectDisposedException("synthetic model");
    }

    private sealed class Hanging : ILanguageModel
    {
        public async Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new("");
        }
    }

    [Fact]
    public async Task Summarize_ScreenshotRefused_AsksAgainWithTextAlone()
    {
        var model = new ImageRefusing();
        var summarizer = new ContextSummarizer(model, () => null, TimeSpan.FromSeconds(1));

        var summary = await summarizer.SummarizeAsync(
            Task.FromResult(Read with { Screenshot = [0xFF, 0xD8] }), CancellationToken.None);

        Assert.Equal("They write.", summary.Text);
        Assert.Equal([true, false], model.WithImage);
    }

    private sealed class ImageRefusing : ILanguageModel
    {
        public List<bool> WithImage { get; } = [];

        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
        {
            WithImage.Add(prompt.Image is not null);
            return Task.FromResult(prompt.Image is null
                ? new CompletionResult("They write.")
                : new CompletionResult("", new(CompletionError.Rejected, 400)));
        }
    }
}
