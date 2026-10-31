using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Exceptions;
using LLama.Native;
using LLama.Sampling;
using Naad.Native;
using Naad.Transcription;

namespace Naad.LanguageModel;

/// <summary>
/// Runs a language model on this PC with llama.cpp through LLamaSharp. Nothing leaves the
/// machine. It runs on the graphics card through Vulkan when the PC has a Vulkan driver (llama.cpp
/// prefers a discrete card over an integrated one by itself) and on the processor otherwise. The
/// model loads on first use and unloads after <see cref="IdleUnload"/>, so Naad doesn't hold a
/// gigabyte or more all day. One prompt runs at a time.
/// </summary>
internal sealed class LocalLanguageModel : ILanguageModel, IDisposable
{
    /// <summary>
    /// Long enough to stay loaded through a burst of dictations; loading again costs 2 to 5 s.
    /// </summary>
    public static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(10);

    // A cleanup prompt (about 600 tokens), a few minutes of dictation and a reply of the same
    // length fit; the cache this needs is about 100 MB for a 1.5B model.
    private const uint ContextTokens = 4096;

    // Every layer: llama.cpp places what fits on the graphics card and ignores it on the processor.
    private const int AllLayers = 999;

    // Half the logical processors, at most 8, as for Whisper: the rest keeps the PC responsive.
    private static readonly int Threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);

    private static readonly Lock NativeSetup = new();
    private static bool _nativeConfigured;

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _idle;

    // Cancelled by Dispose, so it waits only for a prompt to stop, not to finish.
    private readonly CancellationTokenSource _stopping = new();
    private LLamaWeights? _weights;
    private ModelParams? _parameters;
    private bool _disposed;

    /// <param name="path">A checked GGUF model file (see <see cref="LanguageModelFiles"/>).</param>
    /// <param name="time">Runs the idle timer; tests replace it.</param>
    public LocalLanguageModel(string path, TimeProvider time)
    {
        _path = path;
        _idle = time.CreateTimer(_ => Unload(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Whether the model is in memory now.</summary>
    public bool IsLoaded => _weights is not null;

    public async Task<CompletionResult> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            // Loading and running take seconds of CPU or GPU work: never on the UI thread.
            using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopping.Token);
            return await Task.Run(() => RunAsync(prompt, run.Token), run.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(LocalLanguageModel));
        }
        catch (Exception e) when (e is not ObjectDisposedException)
        {
            // A damaged model, too little memory, or a native failure. The type is all that is logged.
            Log.Error(LogEvent.LocalLanguageModelFailed, e);
            return new("", new(CompletionError.LocalFailed));
        }
        finally
        {
            if (!_disposed)
            {
                _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
            }

            _gate.Release();
        }
    }

    /// <summary>
    /// Loads the model now, so a prompt sent a few seconds later doesn't wait for it: a dictation
    /// calls this when recording starts. A failure is logged and left for the prompt to report.
    /// </summary>
    public async Task PreloadAsync()
    {
        if (_weights is not null || !await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!_disposed && _weights is null && File.Exists(_path))
            {
                _idle.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _weights = await Task.Run(Load);
            }
        }
        catch (Exception e)
        {
            Log.Error(LogEvent.LocalLanguageModelFailed, e);
        }
        finally
        {
            if (!_disposed)
            {
                _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
            }

            _gate.Release();
        }
    }

    /// <summary>
    /// Stops a running prompt and frees the model. Waits for a model that is loading, which can
    /// take seconds, so call it off the UI thread.
    /// </summary>
    public void Dispose()
    {
        if (!_stopping.IsCancellationRequested)
        {
            _stopping.Cancel();
        }

        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _idle.Dispose();
            _weights?.Dispose();
            _weights = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void ConfigureNative()
    {
        lock (NativeSetup)
        {
            if (_nativeConfigured)
            {
                return;
            }

            // Must run before LLamaSharp's first native call. llama.cpp's own log lines go nowhere:
            // they name files and devices, and Naad's log takes only its own events.
            NativeLibraryConfig.All.WithVulkan(true).WithCuda(false).WithLogCallback((_, _) => { });
            _nativeConfigured = true;
        }
    }

    private async Task<CompletionResult> RunAsync(ChatPrompt prompt, CancellationToken cancellationToken)
    {
        var weights = _weights ??= Load();
        var template = new LLamaTemplate(weights) { AddAssistant = true };
        template.Add("system", prompt.Instructions);
        template.Add("user", prompt.Text);
        var executor = new StatelessExecutor(weights, _parameters!);
        var inference = new InferenceParams
        {
            MaxTokens = prompt.MaxTokens,

            // Zero: cleanup wants the most likely wording, not variety.
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0 },
        };

        var reply = new StringBuilder();
        await foreach (var piece in executor.InferAsync(
            Encoding.UTF8.GetString(template.Apply()), inference, cancellationToken))
        {
            reply.Append(piece);
        }

        return new(ReplyText.Clean(reply.ToString()));
    }

    private LLamaWeights Load()
    {
        // llama.cpp reports a missing file only as a failed load, which would be retried on the processor.
        if (!File.Exists(_path))
        {
            throw new FileNotFoundException("The language model isn't downloaded.");
        }

        ConfigureNative();
        var started = Stopwatch.GetTimestamp();
        _parameters = new ModelParams(_path)
        {
            ContextSize = ContextTokens,
            GpuLayerCount = AllLayers,
            Threads = Threads,
        };

        LLamaWeights weights;
        try
        {
            weights = LLamaWeights.LoadFromFile(_parameters);
        }
        catch (LoadWeightsFailedException)
        {
            // The graphics card may lack the memory for it, as when a game holds most of it; the
            // processor is slower but always has room.
            Log.Warn(LogEvent.LocalLanguageModelGpuFailed);
            _parameters.GpuLayerCount = 0;
            weights = LLamaWeights.LoadFromFile(_parameters);
        }

        var devices = Ggml.LlamaDeviceTypes();
        Log.Info(
            LogEvent.LocalLanguageModelLoaded,
            ("on_gpu", _parameters.GpuLayerCount > 0 && devices.Any(d => d != GgmlDeviceType.Cpu) ? 1 : 0),
            ("discrete_gpu", devices.Contains(GgmlDeviceType.Gpu) ? 1 : 0),
            ("load_ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds));
        return weights;
    }

    // From the timer's thread. Skipped while a prompt runs; that prompt restarts the timer.
    private void Unload()
    {
        if (!_gate.Wait(0))
        {
            return;
        }

        try
        {
            if (_weights is null || _disposed)
            {
                return;
            }

            _weights.Dispose();
            _weights = null;
            Log.Info(LogEvent.LocalLanguageModelUnloaded);
        }
        finally
        {
            _gate.Release();
        }
    }
}
