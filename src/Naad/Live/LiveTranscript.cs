using Naad.Audio;
using Naad.Transcription;

namespace Naad.Live;

/// <summary>
/// Transcribes a dictation piece by piece while it is still being recorded: each pause the
/// <see cref="SpeechChunker"/> finds sends the piece before it to the transcriber, so on release
/// only the last piece is left. The pieces' text is user content: it is shown, never logged.
/// <see cref="Add"/> runs on the capture thread; the transcriptions run on the thread pool, one
/// at a time and in order.
/// </summary>
/// <param name="transcriber">Transcribes each piece; Whisper on this PC.</param>
/// <param name="chunker">Finds the pauses.</param>
internal sealed class LiveTranscript(ITranscriber transcriber, SpeechChunker chunker) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly MemoryStream _pending = new();
    private readonly List<string> _pieces = [];
    private readonly CancellationTokenSource _stop = new();
    private Task _work = Task.CompletedTask;
    private long _pendingStart;
    private volatile bool _failed;

    /// <summary>
    /// The words heard so far, after each piece is transcribed. Raised on a thread-pool thread;
    /// user content, never logged.
    /// </summary>
    public event Action<string>? TextChanged;

    /// <summary>The next audio from the microphone, in <see cref="AudioRecorder.Format"/>.</summary>
    public void Add(byte[] pcm)
    {
        lock (_gate)
        {
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            _pending.Write(pcm);
            foreach (var cut in chunker.Add(pcm))
            {
                var piece = _pending.GetBuffer().AsSpan(0, (int)(cut - _pendingStart)).ToArray();
                var rest = _pending.GetBuffer().AsSpan(piece.Length, (int)_pending.Length - piece.Length).ToArray();
                _pending.SetLength(0);
                _pending.Write(rest);
                _pendingStart = cut;
                _work = _work.ContinueWith(
                    _ => TranscribeAsync(piece), CancellationToken.None, TaskContinuationOptions.None,
                    TaskScheduler.Default).Unwrap();
            }
        }
    }

    /// <summary>
    /// The whole dictation's text, once the pieces in flight are done and the rest of
    /// <paramref name="pcm"/> (the full recording) is transcribed. If any piece failed, the whole
    /// recording is transcribed again in one pass, so no words are lost to a bad piece.
    /// </summary>
    public async Task<TranscriptionResult> FinishAsync(byte[] pcm, CancellationToken cancellationToken)
    {
        Task work;
        long start;
        lock (_gate)
        {
            work = _work;
            start = _pendingStart;
        }

        await work.WaitAsync(cancellationToken);
        if (_failed || start > pcm.Length)
        {
            Log.Warn(LogEvent.LiveFellBack, ("pieces", _pieces.Count));
            return await transcriber.TranscribeAsync(Wav.Encode(pcm, AudioRecorder.Format), cancellationToken);
        }

        var tail = pcm.AsSpan((int)start);
        var tailBytes = tail.Length;
        var tailWav = SilenceGate.Check(tail, AudioRecorder.Format).Verdict == RecordingVerdict.Accepted
            ? Wav.Encode(tail, AudioRecorder.Format)
            : null;
        if (tailWav is not null)
        {
            var last = await transcriber.TranscribeAsync(tailWav, cancellationToken);
            if (last.Failure is not null)
            {
                return last;
            }

            Keep(last.Text);
        }

        Log.Info(LogEvent.LiveFinished, ("pieces", _pieces.Count), ("tail_bytes", tailBytes));
        return new(Joined());
    }

    /// <summary>Stops transcribing pieces; one already running finishes but is ignored.</summary>
    // ponytail: _stop is cancelled but never disposed, since a piece may still read its token;
    // it holds no timer, so nothing leaks.
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_stop.IsCancellationRequested)
            {
                _stop.Cancel();
            }
        }
    }

    private async Task TranscribeAsync(byte[] piece)
    {
        if (_stop.IsCancellationRequested || _failed)
        {
            return;
        }

        // A piece of noise is never sent: it has no words, and Whisper invents some.
        if (SilenceGate.Check(piece, AudioRecorder.Format).Verdict != RecordingVerdict.Accepted)
        {
            return;
        }

        try
        {
            var result = await transcriber.TranscribeAsync(Wav.Encode(piece, AudioRecorder.Format), _stop.Token);

            // Whisper may finish a short piece before it notices the cancellation.
            if (_stop.IsCancellationRequested)
            {
                return;
            }

            if (result.Failure is not null)
            {
                _failed = true;
                return;
            }

            var text = Keep(result.Text);
            if (text.Length > 0)
            {
                TextChanged?.Invoke(Joined());
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            // A chain of continuations: anything not caught here would vanish silently.
            _failed = true;
            Log.Error(LogEvent.LiveFellBack, e);
        }
    }

    private string Keep(string text)
    {
        text = text.Trim();
        if (text.Length > 0)
        {
            lock (_pieces)
            {
                _pieces.Add(text);
            }
        }

        return text;
    }

    private string Joined()
    {
        lock (_pieces)
        {
            return string.Join(" ", _pieces);
        }
    }
}
