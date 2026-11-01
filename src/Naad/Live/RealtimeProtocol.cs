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

    /// <summary>Gemini Live, over a WebSocket.</summary>
    Gemini,

    /// <summary>xAI's streaming speech-to-text, over a WebSocket.</summary>
    XAI,

    /// <summary>An OpenAI-compatible realtime server at the custom base URL.</summary>
    Custom,
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

    /// <summary>The server is ready for audio (Gemini, xAI): audio sent sooner is refused.</summary>
    Ready,

    /// <summary>The audio left to commit was empty, so there is nothing more to wait for (OpenAI).</summary>
    NothingToCommit,
}

/// <summary>One message from a streaming server. The text is user content: never logged.</summary>
internal readonly record struct RealtimeEvent(RealtimeEventKind Kind, string? Item = null, string Text = "");

/// <summary>
/// The streaming protocols: the address, the messages Naad sends and what the server's messages
/// mean. OpenAI's realtime protocol also serves the custom endpoint. Pure: it builds and reads
/// JSON, and never touches the network.
/// </summary>
internal static class RealtimeProtocol
{
    private const string GeminiEndpoint = "wss://generativelanguage.googleapis.com/ws/"
        + "google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";

    /// <summary>
    /// Models suggested for each service, the default first. Custom has none: an empty model lets
    /// the server use its own.
    /// </summary>
    public static IReadOnlyList<string> SuggestedModels(LiveService service) => service switch
    {
        LiveService.OpenAI => ["gpt-4o-mini-transcribe", "gpt-4o-transcribe"],
        LiveService.Deepgram => ["nova-3", "nova-2"],
        LiveService.Gemini => ["gemini-live-2.5-flash-preview", "gemini-2.0-flash-live-001"],
        LiveService.XAI => ["grok-stt"],
        _ => [],
    };

    /// <summary>The default model of each service, when the user names none; empty for Custom.</summary>
    public static string DefaultModel(LiveService service) =>
        SuggestedModels(service) is [var first, ..] ? first : "";

    /// <summary>The OpenAI protocol takes 24 kHz audio; the others take the recorder's 16 kHz as it is.</summary>
    public static int SampleRate(LiveService service) => SpeaksOpenAi(service) ? 24000 : 16000;

    /// <summary>Whether audio sent before the server says <see cref="RealtimeEventKind.Ready"/> is refused.</summary>
    public static bool NeedsReady(LiveService service) => service is LiveService.Gemini or LiveService.XAI;

    /// <summary>
    /// The WebSocket address for <paramref name="model"/> and the spoken language (null detects
    /// it), or null when <paramref name="baseUrl"/> can't be turned into one.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="model">The model; empty for the server's own.</param>
    /// <param name="language">An ISO 639-1 code, or null.</param>
    /// <param name="baseUrl">For OpenAI and Custom, the API root it is built from.</param>
    public static Uri? Endpoint(LiveService service, string model, string? language, Uri? baseUrl) =>
        service switch
        {
            LiveService.OpenAI or LiveService.Custom => baseUrl is null ? null : RealtimeUrl(baseUrl),
            LiveService.Deepgram => new("wss://api.deepgram.com/v1/listen?model=" + Uri.EscapeDataString(model)
                + "&encoding=linear16&sample_rate=16000&channels=1&interim_results=true&smart_format=true"
                + "&language=" + Uri.EscapeDataString(language ?? "multi")),
            LiveService.Gemini => new(GeminiEndpoint),
            LiveService.XAI => new("wss://api.x.ai/v1/stt?model=" + Uri.EscapeDataString(model)
                + "&sample_rate=16000&encoding=pcm&interim_results=true"
                + (language is null ? "" : "&language=" + Uri.EscapeDataString(language))),
            _ => throw new ArgumentOutOfRangeException(nameof(service)),
        };

    /// <summary>
    /// An OpenAI-compatible API root as its realtime WebSocket: https becomes wss (http, on this
    /// PC, ws), "/v1/realtime" is added unless the root already ends in /v1, and the transcription
    /// intent is asked for. Null for any other scheme.
    /// </summary>
    public static Uri? RealtimeUrl(Uri baseUrl)
    {
        var scheme = baseUrl.Scheme switch
        {
            "https" or "wss" => "wss",
            "http" or "ws" => "ws",
            _ => null,
        };
        if (scheme is null)
        {
            return null;
        }

        var path = baseUrl.AbsolutePath.TrimEnd('/');
        path += path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? "/realtime" : "/v1/realtime";
        var query = baseUrl.Query.TrimStart('?');
        if (!query.Split('&').Any(p => p.StartsWith("intent=", StringComparison.OrdinalIgnoreCase)))
        {
            query = query.Length == 0 ? "intent=transcription" : query + "&intent=transcription";
        }

        // No user name or password: a key typed into the address belongs in the key field.
        return new UriBuilder(baseUrl)
        {
            Scheme = scheme,
            Path = path,
            Query = query,
            Port = baseUrl.Port,
            UserName = "",
            Password = "",
        }.Uri;
    }

    /// <summary>
    /// The header that carries <paramref name="key"/>, or null when there is none to send (a
    /// custom server on this PC may need no key).
    /// </summary>
    public static (string Name, string Value)? KeyHeader(LiveService service, string? key) => key is null
        ? null
        : service switch
        {
            LiveService.Deepgram => ("Authorization", "Token " + key),
            LiveService.Gemini => ("x-goog-api-key", key),
            _ => ("Authorization", "Bearer " + key),
        };

    /// <summary>
    /// The message that sets up the session right after connecting, or null when the address
    /// already says everything (Deepgram). OpenAI's server finds the pauses itself and finishes
    /// each utterance there, so words arrive while the user is still speaking.
    /// </summary>
    public static string? Setup(LiveService service, string model, string? language)
    {
        if (service == LiveService.Gemini)
        {
            return GeminiSetup(model, language);
        }

        if (!SpeaksOpenAi(service))
        {
            return null;
        }

        // An empty model leaves the choice to a custom server.
        var transcription = new JsonObject();
        if (model.Length > 0)
        {
            transcription["model"] = model;
        }

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

    /// <summary>
    /// The message carrying <paramref name="pcm"/> (at <see cref="SampleRate"/>), or null when the
    /// raw bytes go as a binary frame instead (Deepgram, xAI).
    /// </summary>
    public static string? AppendAudio(LiveService service, ReadOnlySpan<byte> pcm) => service switch
    {
        LiveService.Gemini => new JsonObject
        {
            ["realtimeInput"] = new JsonObject
            {
                ["audio"] = new JsonObject
                {
                    ["data"] = Convert.ToBase64String(pcm),
                    ["mimeType"] = "audio/pcm;rate=16000",
                },
            },
        }.ToJsonString(),
        _ when SpeaksOpenAi(service) => new JsonObject
        {
            ["type"] = "input_audio_buffer.append",
            ["audio"] = Convert.ToBase64String(pcm),
        }.ToJsonString(),
        _ => null,
    };

    /// <summary>The message that says the audio has ended.</summary>
    public static string Finish(LiveService service) => service switch
    {
        LiveService.Deepgram => """{"type":"CloseStream"}""",
        LiveService.Gemini => """{"realtimeInput":{"audioStreamEnd":true}}""",
        LiveService.XAI => """{"type":"audio.done"}""",
        _ => """{"type":"input_audio_buffer.commit"}""",
    };

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

            return service switch
            {
                LiveService.Deepgram => ParseDeepgram(root),
                LiveService.Gemini => ParseGemini(root),
                LiveService.XAI => ParseXai(root),
                _ => ParseOpenAi(root),
            };
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

    private static bool SpeaksOpenAi(LiveService service) => service is LiveService.OpenAI or LiveService.Custom;

    // Gemini Live answers in text as well, which Naad ignores: only what it heard is used.
    private static string GeminiSetup(string model, string? language)
    {
        var transcription = new JsonObject();
        if (language is not null)
        {
            transcription["languageCodes"] = new JsonArray(language);
        }

        return new JsonObject
        {
            ["setup"] = new JsonObject
            {
                ["model"] = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model,
                ["generationConfig"] = new JsonObject { ["responseModalities"] = new JsonArray("TEXT") },
                ["inputAudioTranscription"] = transcription,
            },
        }.ToJsonString();
    }

    // Each final piece of what was heard is its own utterance; the interim one replaces the last.
    // ponytail: pieces are joined with a space; if Gemini ever sends parts of words, join them as sent.
    private static RealtimeEvent ParseGemini(JsonObject root)
    {
        if (root.ContainsKey("setupComplete"))
        {
            return new(RealtimeEventKind.Ready);
        }

        if (root["serverContent"] is not JsonObject content)
        {
            return new(RealtimeEventKind.Other);
        }

        if ((string?)content["inputTranscription"]?["text"] is { } heard)
        {
            return new(RealtimeEventKind.Final, null, heard.Trim());
        }

        if ((string?)content["interimInputTranscription"]?["text"] is { } interim)
        {
            return new(RealtimeEventKind.Partial, null, interim.TrimStart());
        }

        return content["turnComplete"]?.GetValue<bool>() == true
            ? new(RealtimeEventKind.Done)
            : new(RealtimeEventKind.Other);
    }

    // Each partial repeats the utterance so far; it is final once both flags say so.
    private static RealtimeEvent ParseXai(JsonObject root) => (string?)root["type"] switch
    {
        "transcript.created" => new(RealtimeEventKind.Ready),
        "transcript.partial" when root["is_final"]?.GetValue<bool>() == true
            && root["speech_final"]?.GetValue<bool>() == true =>
            new(RealtimeEventKind.Final, null, ((string?)root["text"] ?? "").Trim()),
        "transcript.partial" => new(RealtimeEventKind.Partial, null, ((string?)root["text"] ?? "").Trim()),
        "transcript.done" => new(RealtimeEventKind.Done),
        "error" => new(RealtimeEventKind.Error),
        _ => new(RealtimeEventKind.Other),
    };

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
