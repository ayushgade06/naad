using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Naad.Transcription;

namespace Naad.Tests.Transcription;

// Every download runs against a fake HttpMessageHandler with a few invented bytes standing in
// for the model; nothing touches the network. Files go to a temporary directory.
public sealed class LocalModelTests : IDisposable
{
    private static readonly byte[] FakeModel = [.. Enumerable.Range(0, 200_000).Select(i => (byte)(i * 7))];
    private static readonly Uri Source = new("https://models.example.test/ggml-test.bin");

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "naad-model-tests", Guid.NewGuid().ToString("N"));

    private string ModelPath => Path.Combine(_directory, "models", "ggml-test.bin");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Base_PointsAtThePublishedBaseModel()
    {
        Assert.Equal(
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            LocalModel.Base.Source.AbsoluteUri);
        Assert.EndsWith(
            Path.Combine("Naad", "models", "ggml-base.bin"), LocalModel.Base.Path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_MatchingFile_PutsItInPlaceAndReportsProgress()
    {
        var model = Model(Sha(FakeModel));
        var progress = new List<double>();

        var result = await model.DownloadAsync(Http(FakeModel), new SyncProgress(progress.Add), CancellationToken.None);

        Assert.Equal(new ModelDownloadResult(ModelDownloadOutcome.Downloaded), result);
        Assert.True(model.IsReady());
        Assert.Equal(FakeModel, File.ReadAllBytes(ModelPath));
        Assert.Equal(1.0, progress[^1]);
        Assert.Equal(progress.Order(), progress);
        Assert.False(File.Exists(ModelPath + ".download"));
    }

    [Fact]
    public async Task Download_ChecksumMismatch_KeepsNothing()
    {
        var model = Model(Sha([1, 2, 3]));

        var result = await model.DownloadAsync(Http(FakeModel), null, CancellationToken.None);

        Assert.Equal(new ModelDownloadResult(ModelDownloadOutcome.ChecksumMismatch), result);
        Assert.False(model.IsReady());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ModelPath)!));
    }

    [Fact]
    public async Task Download_HttpError_ReturnsStatus()
    {
        var model = Model(Sha(FakeModel));

        var result = await model.DownloadAsync(
            Http(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), null, CancellationToken.None);

        Assert.Equal(new ModelDownloadResult(ModelDownloadOutcome.HttpError, 404), result);
        Assert.False(File.Exists(ModelPath));
    }

    [Fact]
    public async Task Download_NoConnection_ReturnsNetwork()
    {
        var model = Model(Sha(FakeModel));

        var result = await model.DownloadAsync(
            Http(_ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "no host")),
            null,
            CancellationToken.None);

        Assert.Equal(
            new ModelDownloadResult(ModelDownloadOutcome.Network, (int)HttpRequestError.NameResolutionError), result);
    }

    [Fact]
    public async Task Download_Cancelled_ThrowsAndKeepsNothing()
    {
        var model = Model(Sha(FakeModel));
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.DownloadAsync(Http(FakeModel), null, cancel.Token));
        Assert.False(File.Exists(ModelPath));
        Assert.False(File.Exists(ModelPath + ".download"));
    }

    [Fact]
    public async Task Download_ReplacesAWrongSizedFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        File.WriteAllBytes(ModelPath, [9, 9, 9]);
        var model = Model(Sha(FakeModel));
        Assert.False(model.IsReady());

        await model.DownloadAsync(Http(FakeModel), null, CancellationToken.None);

        Assert.True(model.IsReady());
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static HttpClient Http(byte[] body) =>
        Http(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });

    private static HttpClient Http(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));

    private LocalModel Model(string sha256) => new(ModelPath, Source, sha256, FakeModel.Length);

    // Progress<T> posts to the thread pool; this one records in order, at once.
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return Task.FromResult(respond(request));
            }
            catch (Exception e) when (e is not UnreachableException)
            {
                return Task.FromException<HttpResponseMessage>(e);
            }
        }
    }
}
