using Naad.LanguageModel;
using Naad.Transcription;

namespace Naad.Tests.Providers;

public sealed class OfflineFallbackTests
{
    [Fact]
    public async Task Transcribe_Offline_UsesThisPcAndSaysSo()
    {
        var cloud = new Transcriber(new("cloud"));
        var local = new Transcriber(new("local"));
        var told = 0;
        using var fallback = new OfflineFallbackTranscriber(cloud, () => local, () => false, () => told++);

        var result = await fallback.TranscribeAsync([1], CancellationToken.None);

        Assert.Equal(("local", 0, 1), (result.Text, cloud.Calls, told));
    }

    [Theory]
    [InlineData("Network")]
    [InlineData("NoInternet")]
    public async Task Transcribe_CloudUnreachable_RetriesOnThisPcWithTheSameAudio(string error)
    {
        var cloud = new Transcriber(new("", new(Enum.Parse<TranscriptionError>(error))));
        var local = new Transcriber(new("local"));
        using var fallback = new OfflineFallbackTranscriber(cloud, () => local, () => true, () => { });

        var result = await fallback.TranscribeAsync([7, 8], CancellationToken.None);

        Assert.Equal("local", result.Text);
        Assert.Equal([7, 8], local.Audio.Single());
    }

    [Fact]
    public async Task Transcribe_CloudRejectsTheKey_ReportsItWithoutFallingBack()
    {
        var cloud = new Transcriber(new("", new(TranscriptionError.Unauthorized)));
        var local = new Transcriber(new("local"));
        using var fallback = new OfflineFallbackTranscriber(cloud, () => local, () => true, () => { });

        var result = await fallback.TranscribeAsync([1], CancellationToken.None);

        Assert.Equal(TranscriptionError.Unauthorized, result.Failure?.Error);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Transcribe_OfflineWithoutLocalModel_TriesTheCloud()
    {
        var cloud = new Transcriber(new("", new(TranscriptionError.NoInternet)));
        using var fallback = new OfflineFallbackTranscriber(cloud, () => null, () => false, () => { });

        var result = await fallback.TranscribeAsync([1], CancellationToken.None);

        Assert.Equal(TranscriptionError.NoInternet, result.Failure?.Error);
    }

    [Fact]
    public async Task Complete_Offline_UsesThisPc()
    {
        var local = new Model(new("local"));
        using var fallback = new OfflineFallbackLanguageModel(
            new Model(new("cloud")), () => local, () => false, () => { });

        var result = await fallback.CompleteAsync(new("i", "t"), CancellationToken.None);

        Assert.Equal("local", result.Text);
    }

    [Fact]
    public async Task Complete_CloudUnreachable_UsesThisPc()
    {
        var cloud = new Model(new("", new(CompletionError.Network)));
        using var fallback = new OfflineFallbackLanguageModel(
            cloud, () => new Model(new("local")), () => true, () => { });

        Assert.Equal("local", (await fallback.CompleteAsync(new("i", "t"), CancellationToken.None)).Text);
    }

    private sealed class Transcriber(TranscriptionResult result) : ITranscriber
    {
        public List<byte[]> Audio { get; } = [];

        public int Calls => Audio.Count;

        public Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
        {
            Audio.Add(wav);
            return Task.FromResult(result);
        }
    }

    private sealed class Model(CompletionResult result) : ILanguageModel
    {
        public Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
