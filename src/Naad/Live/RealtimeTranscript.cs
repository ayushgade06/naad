using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Naad.Audio;
using Naad.Transcription;

namespace Naad.Live;

/// <summary>
/// Streams a dictation to a cloud service's realtime transcription while it is recorded, so
/// the words arrive as the user speaks. Unless the stream ends with every word accounted for
/// (see <see cref="RealtimeText.Complete"/>), the whole recording is uploaded through
/// <paramref name="upload"/> instead, so no words are lost. <see cref="Add"/> runs on the capture
/// thread; the socket on the thread pool. Sends the audio and the API key to the chosen service
/// only; logs event names, counts and exception types, never text.
/// </summary>
/// <param name="service">The streaming service.</param>
/// <param name="endpoint">Its WebSocket address, from <see cref="RealtimeProtocol.Endpoint"/>.</param>
/// <param name="key">The service's API key; null for a custom server that needs none.</param>
/// <param name="model">The service's streaming model; empty for a custom server's own.</param>
/// <param name="language">An ISO 639-1 code, or null to detect it.</param>
/// <param name="upload">The regular transcriber, used when streaming fails.</param>
internal sealed class RealtimeTranscript(
    LiveService service, Uri endpoint, string? key, string model, string? language, ITranscriber upload)
    : ILiveSession
{
    // Connecting takes a few hundred milliseconds; past this the network is too slow to stream.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // After the audio ends, the server's last words normally follow within a second or two;
    // past this the recording is uploaded instead.
    private static readonly TimeSpan LongestFinish = TimeSpan.FromSeconds(8);

    // A server that refuses the model does so within this long of the first audio.
    private static readonly TimeSpan TestSettle = TimeSpan.FromSeconds(1.5);

    // How often the end is checked while waiting for it.
    private static readonly TimeSpan EndPoll = TimeSpan.FromMilliseconds(100);

    // Servers' messages are small JSON; a longer one arrives in several parts.
    private const int ReceiveBufferBytes = 16 * 1024;

    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly RealtimeText _text = new();
    private readonly Lock _gate = new();
    private readonly ClientWebSocket _socket = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _run;
    private string _shownPreview = "";

    /// <inheritdoc/>
    public bool FellBack { get; private set; }

    public event Action<string>? TextChanged;

    public event Action<string>? StableChanged;

    /// <summary>Connects and starts sending; audio added before the connection is ready waits.</summary>
    public void Start() => _run = Task.Run(RunAsync);

    public void Add(byte[] pcm)
    {
        // After a failure nothing reads the audio; it is uploaded at the end instead.
        if (!_stop.IsCancellationRequested && !Failed)
        {
            _audio.Writer.TryWrite(pcm);
        }
    }

    public async Task<TranscriptionResult> FinishAsync(byte[] pcm, CancellationToken cancellationToken)
    {
        _audio.Writer.TryComplete();
        try
        {
            if (_run is { } run)
            {
                // A service that says when it is ready may take a second connect timeout to say so.
                var connecting = RealtimeProtocol.NeedsReady(service) ? ConnectTimeout * 2 : ConnectTimeout;
                await run.WaitAsync(LongestFinish + connecting, cancellationToken);
            }
        }
        catch (TimeoutException)
        {
        }

        // Only when the stream ended with every utterance final: anything less could drop words.
        string text;
        bool complete;
        lock (_gate)
        {
            complete = _text.Complete;
            text = complete ? _text.Stable : "";
        }

        if (text.Length > 0)
        {
            Log.Info(LogEvent.LiveFinished, ("chars", text.Length));
            return new(text);
        }

        Log.Warn(LogEvent.LiveFellBack);

        // A stream that ended well but heard nothing didn't fail; the upload only double-checks it.
        FellBack = !complete;
        return await upload.TranscribeAsync(Wav.Encode(pcm, AudioRecorder.Format), cancellationToken);
    }

    public void Dispose()
    {
        _audio.Writer.TryComplete();
        _stop.Cancel();
        _socket.Abort();
    }

    private bool Failed
    {
        get
        {
            lock (_gate)
            {
                return _text.Failed;
            }
        }
    }

    private async Task RunAsync()
    {
        try
        {
            var receiving = await ConnectAsync();
            await foreach (var pcm in _audio.Reader.ReadAllAsync(_stop.Token))
            {
                if (Failed)
                {
                    break;
                }

                await SendAudioAsync(pcm);
            }

            // The server already broke off: telling it the audio ended would only fail again.
            if (Failed)
            {
                return;
            }

            lock (_gate)
            {
                _text.FinishSent();
            }

            await SendTextAsync(RealtimeProtocol.Finish(service));
            await WaitForEndAsync(receiving);
        }
        catch (Exception e)
        {
            // Any failure, even one from a socket aborted mid-call, means the recording is uploaded.
            Fail();

            // A short tap or Esc disposes the stream on purpose. Otherwise the type only: a
            // handshake error's message can carry the server's reply.
            if (!_stop.IsCancellationRequested)
            {
                Log.Error(LogEvent.LiveStreamFailed, e);
            }
        }
        finally
        {
            _socket.Abort();
        }
    }

    /// <summary>
    /// Whether the service accepts a stream with these choices: it connects, takes the setup and
    /// a fifth of a second of silence, and is still there a moment later, as some refuse a model
    /// only once audio arrives. Returns null when it does, or why not, in a sentence.
    /// </summary>
    public async Task<string?> TestAsync(CancellationToken cancellationToken)
    {
        using var stop = cancellationToken.Register(Dispose);
        try
        {
            await ConnectAsync();
            await SendAudioAsync(new byte[AudioRecorder.Format.AverageBytesPerSecond / 5]);
            await Task.Delay(TestSettle, _stop.Token);
            return Failed || _socket.State != WebSocketState.Open
                ? "The service refused the stream. Check the key and the live model."
                : null;
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or TimeoutException
            && !cancellationToken.IsCancellationRequested)
        {
            Log.Error(LogEvent.LiveStreamFailed, e);
            return _socket.HttpStatusCode is var status and not 0 and not System.Net.HttpStatusCode.SwitchingProtocols
                ? $"The service refused the connection (HTTP {(int)status}). Check the key and the address."
                : "Naad couldn't connect to the live service.";
        }
        finally
        {
            Dispose();
        }
    }

    // Connects, sets the session up and, for a service that says when it is ready, waits for that.
    // Returns the task receiving the server's messages.
    private async Task<Task> ConnectAsync()
    {
        _socket.Options.CollectHttpResponseDetails = true;
        if (RealtimeProtocol.KeyHeader(service, key) is var (name, value))
        {
            _socket.Options.SetRequestHeader(name, value);
        }

        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            connect.CancelAfter(ConnectTimeout);
            await _socket.ConnectAsync(endpoint, connect.Token);
        }

        var receiving = ReceiveAsync();
        if (RealtimeProtocol.Setup(service, model, language) is { } setup)
        {
            await SendTextAsync(setup);
        }

        // Audio sent sooner is refused; it waits in the queue meanwhile.
        if (RealtimeProtocol.NeedsReady(service))
        {
            await _ready.Task.WaitAsync(ConnectTimeout, _stop.Token);
        }

        return receiving;
    }

    private Task SendAudioAsync(byte[] pcm)
    {
        var audio = RealtimeProtocol.SampleRate(service) == 24000 ? Resample.To24k(pcm) : pcm;
        return RealtimeProtocol.AppendAudio(service, audio) is { } json
            ? SendTextAsync(json)
            : _socket.SendAsync(audio, WebSocketMessageType.Binary, true, _stop.Token);
    }

    // Until the stream is complete, has failed, the receiver stopped, or time is up.
    private async Task WaitForEndAsync(Task receiving)
    {
        var deadline = Environment.TickCount64 + (long)LongestFinish.TotalMilliseconds;
        while (Environment.TickCount64 < deadline && !receiving.IsCompleted)
        {
            lock (_gate)
            {
                if (_text.Complete || _text.Failed)
                {
                    return;
                }
            }

            await Task.Delay(EndPoll, _stop.Token);
        }
    }

    // Never throws: a broken or closed connection is recorded, and decides nothing on its own.
    private async Task ReceiveAsync()
    {
        var buffer = new byte[ReceiveBufferBytes];
        using var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var part = await _socket.ReceiveAsync(buffer, _stop.Token);
                if (part.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, part.Count);
                if (!part.EndOfMessage)
                {
                    continue;
                }

                var e = RealtimeProtocol.Parse(service, Encoding.UTF8.GetString(message.GetBuffer(), 0,
                    (int)message.Length));
                message.SetLength(0);
                if (e.Kind is RealtimeEventKind.Ready or RealtimeEventKind.Error)
                {
                    _ready.TrySetResult();
                }

                if (e.Kind == RealtimeEventKind.Ready)
                {
                    continue;
                }

                string stable;
                string preview;
                bool changed;
                lock (_gate)
                {
                    changed = _text.Apply(e);
                    (stable, preview) = (_text.Stable, _text.Preview);
                }

                // Only a new preview is redrawn; most of a server's messages change nothing shown.
                if (preview != _shownPreview)
                {
                    _shownPreview = preview;
                    TextChanged?.Invoke(preview);
                }

                if (changed)
                {
                    StableChanged?.Invoke(stable);
                }
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException
            or IOException)
        {
        }

        lock (_gate)
        {
            _text.Closed();
        }

        // A server that closes, or refuses the setup, before saying it is ready never will.
        _ready.TrySetResult();
    }

    private void Fail()
    {
        lock (_gate)
        {
            _text.Apply(new(RealtimeEventKind.Error));
        }
    }

    private Task SendTextAsync(string json) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, _stop.Token);
}
