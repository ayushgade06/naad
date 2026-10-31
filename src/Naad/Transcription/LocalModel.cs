using System.Diagnostics;
using System.Security.Cryptography;

namespace Naad.Transcription;

/// <summary>The Whisper models Naad can run on this PC (see <see cref="LocalModel"/>).</summary>
internal enum WhisperModel
{
    Base,
    Turbo,
}

internal enum ModelDownloadOutcome
{
    Downloaded,

    /// <summary>The file arrived but its SHA-256 doesn't match: damaged or replaced. It was deleted.</summary>
    ChecksumMismatch,

    /// <summary>The server answered with an error status.</summary>
    HttpError,

    /// <summary>No connection, or the connection dropped or stalled.</summary>
    Network,

    /// <summary>The file couldn't be written, for example a full disk.</summary>
    DiskError,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Code">For the log: the HTTP status for HttpError, the HttpRequestError for Network;
/// otherwise 0.</param>
internal readonly record struct ModelDownloadResult(ModelDownloadOutcome Outcome, int Code = 0);

/// <summary>
/// A model file that runs on this PC, Whisper or a language model: where it lives, and its
/// download. A file is only ever put in place after its SHA-256 matches the published one, so a
/// damaged or tampered download is never loaded.
/// </summary>
/// <param name="path">Where the model is kept.</param>
/// <param name="source">Where it is downloaded from.</param>
/// <param name="sha256">The expected SHA-256, lowercase hex.</param>
/// <param name="size">The expected size in bytes.</param>
internal sealed class LocalModel(string path, Uri source, string sha256, long size)
{
    /// <summary>
    /// whisper.cpp's conversion of OpenAI's multilingual Whisper base model: 148 MB, fast enough
    /// on a laptop CPU for dictation, and it handles every language the cloud model does. Size and
    /// SHA-256 are those Hugging Face publishes for this file.
    /// </summary>
    public static LocalModel Base { get; } = Published(
        "ggml-base.bin", "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe", 147_951_465);

    /// <summary>
    /// whisper.cpp's 5-bit conversion of large-v3-turbo, the model Groq runs by default: 574 MB,
    /// and close to the cloud's accuracy. On the maintainer's PC a dictation took about 1.5 s on a
    /// discrete graphics card but 30 s on the processor, so it is offered for PCs with a card.
    /// </summary>
    public static LocalModel Turbo { get; } = Published(
        "ggml-large-v3-turbo-q5_0.bin", "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2",
        574_041_195);

    // A download that delivers nothing for this long has stalled; a slow but moving one never
    // times out, since hundreds of megabytes can take minutes.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private const int BufferBytes = 81_920;

    public string Path => path;

    public static LocalModel For(WhisperModel model) => model switch
    {
        WhisperModel.Base => Base,
        WhisperModel.Turbo => Turbo,
        _ => throw new UnreachableException(),
    };

    public Uri Source => source;

    public long Size => size;

    /// <summary>
    /// Whether the model is in place. Checks the size only: hashing 148 MB at every start would
    /// cost about a second, and the hash was checked before the file was put in place.
    /// </summary>
    public bool IsReady()
    {
        var file = new FileInfo(path);
        return file.Exists && file.Length == size;
    }

    /// <summary>
    /// Downloads the model to a temporary file, checking its SHA-256 as it arrives, and only then
    /// moves it into place. Any failure leaves no partial file behind.
    /// </summary>
    /// <param name="http">Shared and owned by the caller.</param>
    /// <param name="progress">Receives the fraction downloaded, 0 to 1.</param>
    /// <param name="cancellationToken">Stops the download, as when the settings window closes.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<ModelDownloadResult> DownloadAsync(
        HttpClient http, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var temp = path + ".download";
        try
        {
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            using var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (!response.IsSuccessStatusCode)
            {
                return new(ModelDownloadOutcome.HttpError, (int)response.StatusCode);
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var body = await response.Content.ReadAsStreamAsync(stall.Token))
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[BufferBytes];
                long received = 0;
                int read;
                while ((read = await body.ReadAsync(buffer, stall.Token)) > 0)
                {
                    stall.CancelAfter(StallTimeout);
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(Math.Min(1.0, (double)received / size));
                }
            }

            if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), sha256, StringComparison.Ordinal))
            {
                return new(ModelDownloadOutcome.ChecksumMismatch);
            }

            File.Move(temp, path, overwrite: true);
            return new(ModelDownloadOutcome.Downloaded);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(ModelDownloadOutcome.Network);
        }
        catch (HttpRequestException e)
        {
            return new(ModelDownloadOutcome.Network, (int)e.HttpRequestError);
        }
        catch (HttpIOException e)
        {
            // The connection dropped while the file was arriving.
            return new(ModelDownloadOutcome.Network, (int)e.HttpRequestError);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(ModelDownloadOutcome.DiskError);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>A file downloaded from <paramref name="source"/> into Naad's models folder.</summary>
    public static LocalModel InModelsFolder(string file, Uri source, string sha256, long size) => new(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Naad", "models", file),
        source,
        sha256,
        size);

    // A file from whisper.cpp's Hugging Face repository.
    private static LocalModel Published(string file, string sha256, long size) => InModelsFolder(
        file, new Uri($"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{file}"), sha256, size);

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover partial file is overwritten by the next download.
        }
    }
}
