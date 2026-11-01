using System.Text.Json;
using System.Text.Json.Nodes;

namespace Naad.Live;

/// <summary>Where live transcription runs.</summary>
internal enum LiveService
{
    /// <summary>Whisper on this PC, phrase by phrase.</summary>
    Local,

    /// <summary>OpenAI's realtime transcription, over a WebSocket.</summary>
    OpenAI,

    /// <summary>Deepgram's streaming API, over a WebSocket.</summary>
    Deepgram,
}

internal enum RealtimeEventKind
{
    /// <summary>Something Naad doesn't act on.</summary>
    Other,

    /// <summary>The server took a stretch of audio as one utterance (OpenAI).</summary>
    Committed,

    /// <summary>More words of an utterance that may still change.</summary>
    Partial,

    /// <summary>An utterance's final words.</summary>
    Final,

    /// <summary>The server has sent everything (Deepgram's closing metadata).</summary>
    Done,

    /// <summary>The server reported an error; the recording is uploaded instead.</summary>
    Error,

    /// <summary>The audio left to commit was empty, so there is nothing more to wait for (OpenAI).</summary>
    NothingToCommit,
}

/// <summary>One message from a streaming server. The text is user content: never logged.</summary>
internal readonly record struct RealtimeEvent(RealtimeEventKind Kind, string? Item = null, string Text = "");

/// <summary>
/// The two streaming protocols: the address, the messages Naad sends and what the server's
/// messages mean. Pure: it builds and reads JSON, and never touches the network.
/// </summary>
internal static class RealtimeProtocol
{
    /// <summary>The default model of each service, when the user names none.</summary>
    public static string DefaultModel(LiveService service) => service switch
    {
        LiveService.OpenAI => "gpt-4o-mini-transcribe",
        LiveService.Deepgram => "nova-3",
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    /// <summary>OpenAI takes 24 kHz audio; Deepgram takes the recorder's 16 kHz as it is.</summary>
    public static int SampleRate(LiveService service) => service == LiveService.OpenAI ? 24000 : 16000;

    /// <summary>The WebSocket address for <paramref name="model"/> and the spoken language (null detects it).</summary>
    public static Uri Endpoint(LiveService service, string model, string? language) => service switch
    {
        LiveService.OpenAI => new("wss://api.openai.com/v1/realtime?intent=transcription"),
        LiveService.Deepgram => new("wss://api.deepgram.com/v1/listen?model=" + Uri.EscapeDataString(model)
            + "&encoding=linear16&sample_rate=16000&channels=1&interim_results=true&smart_format=true"
            + "&language=" + Uri.EscapeDataString(language ?? "multi")),
        _ => throw new ArgumentOutOfRangeException(nameof(service)),
    };

    /// <summary>The Authorization header's value carrying <paramref name="key"/>.</summary>
    public static string Authorization(LiveService service, string key) =>
        (service == LiveService.Deepgram ? "Token " : "Bearer ") + key;

    /// <summary>
    /// The message that sets up the session right after connecting, or null when the address
    /// already says everything (Deepgram). OpenAI's server finds the pauses itself and finishes
    /// each utterance there, so words arrive while the user is still speaking.
    /// </summary>
    public static string? Setup(LiveService service, string model, string? language)
    {
        if (service != LiveService.OpenAI)
        {
            return null;
        }

        var transcription = new JsonObject { ["model"] = model };
        if (language is not null)
        {
            transcription["language"] = language;
        }

        return new JsonObject
        {
            ["type"] = "session.update",
            ["session"] = new JsonObject
            {
                ["type"] = "transcription",
                ["audio"] = new JsonObject
                {
                    ["input"] = new JsonObject
                    {
                        ["format"] = new JsonObject { ["type"] = "audio/pcm", ["rate"] = 24000 },
                        ["transcription"] = transcription,
                        ["turn_detection"] = new JsonObject
                        {
                            ["type"] = "server_vad",
                            ["silence_duration_ms"] = PauseMilliseconds,
                        },
                    },
                },
            },
        }.ToJsonString();
    }

    /// <summary>OpenAI's message carrying audio; Deepgram takes the raw bytes as a binary frame instead.</summary>
    public static string AppendAudio(ReadOnlySpan<byte> pcm) =>
        new JsonObject
        {
            ["type"] = "input_audio_buffer.append",
            ["audio"] = Convert.ToBase64String(pcm),
        }.ToJsonString();

    /// <summary>The message that says the audio has ended.</summary>
    public static string Finish(LiveService service) => service == LiveService.OpenAI
        ? """{"type":"input_audio_buffer.commit"}"""
        : """{"type":"CloseStream"}""";

    /// <summary>What a server message means; a message that isn't valid JSON is an error.</summary>
    public static RealtimeEvent Parse(LiveService service, string json)
    {
        try
        {
            var root = JsonNode.Parse(json)?.AsObject();
            if (root is null)
            {
                return new(RealtimeEventKind.Error);
            }

            return service == LiveService.OpenAI ? ParseOpenAi(root) : ParseDeepgram(root);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return new(RealtimeEventKind.Error);
        }
    }

    // The pause that ends an utterance: as long as the piece splitter on this PC waits.
    private const int PauseMilliseconds = 600;

    private static RealtimeEvent ParseOpenAi(JsonObject root)
    {
        var item = (string?)root["item_id"];
        return (string?)root["type"] switch
        {
            "input_audio_buffer.committed" => new(RealtimeEventKind.Committed, item),
            "conversation.item.input_audio_transcription.delta" =>
                new(RealtimeEventKind.Partial, item, (string?)root["delta"] ?? ""),
            "conversation.item.input_audio_transcription.completed" =>
                new(RealtimeEventKind.Final, item, ((string?)root["transcript"] ?? "").Trim()),
            "conversation.item.input_audio_transcription.failed" => new(RealtimeEventKind.Error),

            // Committing after the server already finished the last utterance leaves nothing to commit.
            "error" when (string?)root["error"]?["code"] == "input_audio_buffer_commit_empty" =>
                new(RealtimeEventKind.NothingToCommit),
            "error" => new(RealtimeEventKind.Error),
            _ => new(RealtimeEventKind.Other),
        };
    }

    private static RealtimeEvent ParseDeepgram(JsonObject root) => (string?)root["type"] switch
    {
        "Results" => new(
            root["is_final"]?.GetValue<bool>() == true ? RealtimeEventKind.Final : RealtimeEventKind.Partial,
            null,
            ((string?)root["channel"]?["alternatives"]?[0]?["transcript"] ?? "").Trim()),
        "Metadata" => new(RealtimeEventKind.Done),
        "Error" => new(RealtimeEventKind.Error),
        _ => new(RealtimeEventKind.Other),
    };
}
