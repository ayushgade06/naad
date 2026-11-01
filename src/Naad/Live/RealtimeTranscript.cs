using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Naad.Audio;
using Naad.Transcription;

namespace Naad.Live;

/// <summary>
/// Streams a dictation to OpenAI's or Deepgram's realtime transcription while it is recorded, so
/// the words arrive as the user speaks. Unless the stream ends with every word accounted for
/// (see <see cref="RealtimeText.Complete"/>), the whole recording is uploaded through
/// <paramref name="upload"/> instead, so no words are lost. <see cref="Add"/> runs on the capture
/// thread; the socket on the thread pool. Sends the audio and the API key to the chosen service
/// only; logs event names, counts and exception types, never text.
/// </summary>
/// <param name="service">OpenAI or Deepgram.</param>
/// <param name="key">The service's API key.</param>
/// <param name="model">The service's streaming model.</param>
/// <param name="language">An ISO 639-1 code, or null to detect it.</param>
/// <param name="upload">The regular transcriber, used when streaming fails.</param>
internal sealed class RealtimeTranscript(
    LiveService service, string key, string model, string? language, ITranscriber upload) : ILiveSession
{
    // Connecting takes a few hundred milliseconds; past this the network is too slow to stream.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    // After the audio ends, the server's last words normally follow within a second or two;
    // past this the recording is uploaded instead.
    private static readonly TimeSpan LongestFinish = TimeSpan.FromSeconds(8);

    // How often the end is checked while waiting for it.
    private static readonly TimeSpan EndPoll = TimeSpan.FromMilliseconds(100);

    // Servers' messages are small JSON; a longer one arrives in several parts.
    private const int ReceiveBufferBytes = 16 * 1024;

    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(new() { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly RealtimeText _text = new();
    private readonly Lock _gate = new();
    private readonly ClientWebSocket _socket = new();
    private Task? _run;

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
                await run.WaitAsync(LongestFinish + ConnectTimeout, cancellationToken);
            }
        }
        catch (TimeoutException)
        {
        }

        // Only when the stream ended with every utterance final: anything less could drop words.
        string text;
        lock (_gate)
        {
            text = _text.Complete ? _text.Stable : "";
        }

        if (text.Length > 0)
        {
            Log.Info(LogEvent.LiveFinished, ("chars", text.Length));
            return new(text);
        }

        Log.Warn(LogEvent.LiveFellBack);
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
            _socket.Options.SetRequestHeader("Authorization", RealtimeProtocol.Authorization(service, key));
            using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                connect.CancelAfter(ConnectTimeout);
                await _socket.ConnectAsync(RealtimeProtocol.Endpoint(service, model, language), connect.Token);
            }

            if (RealtimeProtocol.Setup(service, model, language) is { } setup)
            {
                await SendTextAsync(setup);
            }

            var receiving = ReceiveAsync();
            await foreach (var pcm in _audio.Reader.ReadAllAsync(_stop.Token))
            {
                if (Failed)
                {
                    break;
                }

                if (service == LiveService.OpenAI)
                {
                    await SendTextAsync(RealtimeProtocol.AppendAudio(Resample.To24k(pcm)));
                }
                else
                {
                    await _socket.SendAsync(pcm, WebSocketMessageType.Binary, true, _stop.Token);
                }
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
                string stable;
                string preview;
                bool changed;
                lock (_gate)
                {
                    changed = _text.Apply(e);
                    (stable, preview) = (_text.Stable, _text.Preview);
                }

                TextChanged?.Invoke(preview);
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
