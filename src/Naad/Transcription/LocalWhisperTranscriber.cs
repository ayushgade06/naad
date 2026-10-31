using System.Diagnostics;
using System.Text;
using Naad.Native;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Naad.Transcription;

/// <summary>
/// Transcribes on this PC with whisper.cpp through Whisper.net. Audio never leaves the machine,
/// and nothing here touches the network. It runs on the graphics card through Vulkan when the PC
/// has a Vulkan driver, preferring a discrete card, and on the processor otherwise. The model
/// loads on the first dictation, not at start, and stays loaded until this transcriber is disposed.
/// </summary>
internal sealed class LocalWhisperTranscriber : ITranscriber, IDisposable
{
    // Half the logical processors, at most 8: whisper.cpp gains little beyond that, and the rest
    // keeps the PC responsive while it runs.
    private static readonly int Threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    private readonly Lazy<WhisperFactory> _factory;
    private readonly string? _language;
    private readonly string? _prompt;

    /// <param name="modelPath">A checked ggml model file (see <see cref="LocalModel"/>).</param>
    /// <param name="language">An ISO 639-1 code such as "en", or null to detect the language.</param>
    /// <param name="prompt">Names and terms to expect; never logged.</param>
    public LocalWhisperTranscriber(string modelPath, string? language, string? prompt)
    {
        _factory = new(() => Load(modelPath));
        _language = language;
        _prompt = prompt;
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Loading the model and running it take seconds of CPU: never on the UI thread.
            return await Task.Run(() => RunAsync(wav, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // A missing or damaged model, or a native failure. The type is all that is logged.
            Log.Error(LogEvent.LocalTranscriptionFailed, e);
            return new("", new(TranscriptionError.LocalFailed));
        }
    }

    public void Dispose()
    {
        if (_factory.IsValueCreated)
        {
            _factory.Value.Dispose();
        }
    }

    private static WhisperFactory Load(string modelPath)
    {
        var started = Stopwatch.GetTimestamp();

        // Loads whisper.cpp's libraries (Vulkan's if the PC has a Vulkan driver, else the
        // processor's), so ggml's device list can be read before the model is put on a device.
        WhisperFactory.GetRuntimeInfo();
        var devices = Ggml.DeviceTypes();
        var device = GpuChoice.Pick(devices);
        var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { GpuDevice = device });
        Log.Info(
            LogEvent.LocalModelLoaded,
            ("vulkan", RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan ? 1 : 0),
            ("discrete_gpu", devices.Contains(GgmlDeviceType.Gpu) ? 1 : 0),
            ("gpu_device", device),
            ("load_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        return factory;
    }

    private async Task<TranscriptionResult> RunAsync(byte[] wav, CancellationToken cancellationToken)
    {
        var builder = _factory.Value.CreateBuilder().WithThreads(Threads);
        builder = _language is null ? builder.WithLanguageDetection() : builder.WithLanguage(_language);
        if (_prompt is not null)
        {
            builder = builder.WithPrompt(_prompt);
        }

        await using var processor = builder.Build();
        var text = new StringBuilder();
        using var audio = new MemoryStream(wav, writable: false);
        await foreach (var segment in processor.ProcessAsync(audio, cancellationToken))
        {
            // A tag such as "[BLANK_AUDIO]" stands for noise between or after the words; it
            // would otherwise be pasted as text.
            if (!FillerFilter.IsNonSpeechTag(segment.Text))
            {
                text.Append(segment.Text);
            }
        }

        var transcript = text.ToString().Trim();
        if (FillerFilter.IsFiller(transcript))
        {
            Log.Info(LogEvent.TranscriptionFiltered, ("chars", transcript.Length));
            return new("");
        }

        return new(transcript);
    }
}
