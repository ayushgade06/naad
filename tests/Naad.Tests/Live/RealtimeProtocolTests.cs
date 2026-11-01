using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Naad.Live;

namespace Naad.Tests.Live;

public sealed class RealtimeProtocolTests
{
    [Fact]
    public void Endpoint_Deepgram_AsksFor16kLinearAudioWithInterimResults()
    {
        var url = RealtimeProtocol.Endpoint(LiveService.Deepgram, "nova-3", "en", null)!.ToString();

        Assert.StartsWith("wss://api.deepgram.com/v1/listen?model=nova-3&encoding=linear16&sample_rate=16000", url);
        Assert.Contains("interim_results=true", url);
        Assert.EndsWith("language=en", url);
    }

    [Fact]
    public void Endpoint_DeepgramWithoutLanguage_DetectsIt() =>
        Assert.EndsWith(
            "language=multi", RealtimeProtocol.Endpoint(LiveService.Deepgram, "nova-3", null, null)!.ToString());

    [Theory]
    [InlineData(nameof(LiveService.OpenAI), "Authorization", "Bearer synthetic-key")]
    [InlineData(nameof(LiveService.Deepgram), "Authorization", "Token synthetic-key")]
    [InlineData(nameof(LiveService.Gemini), "x-goog-api-key", "synthetic-key")]
    [InlineData(nameof(LiveService.XAI), "Authorization", "Bearer synthetic-key")]
    public void KeyHeader_UsesEachServicesScheme(string service, string name, string value) =>
        Assert.Equal((name, value), RealtimeProtocol.KeyHeader(Enum.Parse<LiveService>(service), "synthetic-key"));

    [Fact]
    public void KeyHeader_CustomWithoutKey_SendsNone() =>
        Assert.Null(RealtimeProtocol.KeyHeader(LiveService.Custom, null));

    [Theory]
    [InlineData("https://api.openai.com/v1", "wss://api.openai.com/v1/realtime?intent=transcription")]
    [InlineData("https://api.openai.com/v1/", "wss://api.openai.com/v1/realtime?intent=transcription")]
    [InlineData("https://speech.example.test", "wss://speech.example.test/v1/realtime?intent=transcription")]
    [InlineData("http://localhost:8000/v1", "ws://localhost:8000/v1/realtime?intent=transcription")]
    [InlineData("https://speech.example.test/v1?intent=other", "wss://speech.example.test/v1/realtime?intent=other")]
    public void RealtimeUrl_BaseUrl_BecomesItsRealtimeSocket(string baseUrl, string expected) =>
        Assert.Equal(expected, RealtimeProtocol.RealtimeUrl(new(baseUrl))!.ToString());

    [Fact]
    public void RealtimeUrl_OtherScheme_IsNone() =>
        Assert.Null(RealtimeProtocol.RealtimeUrl(new("ftp://example.test")));

    [Fact]
    public void Endpoint_XAi_Asks16kPcmWithTheModelAndLanguage()
    {
        var url = RealtimeProtocol.Endpoint(LiveService.XAI, "grok-stt", "de", null)!.ToString();

        Assert.Equal(
            "wss://api.x.ai/v1/stt?model=grok-stt&sample_rate=16000&encoding=pcm&interim_results=true&language=de",
            url);
    }

    [Fact]
    public void Endpoint_Gemini_IsTheLiveSocket() =>
        Assert.EndsWith(
            "GenerativeService.BidiGenerateContent",
            RealtimeProtocol.Endpoint(LiveService.Gemini, "m", null, null)!.ToString());

    [Fact]
    public void Setup_Gemini_AsksToTranscribeWhatItHears()
    {
        var setup = JsonNode.Parse(RealtimeProtocol.Setup(LiveService.Gemini, "gemini-live-2.5-flash-preview", "hi")!)!;

        Assert.Equal("models/gemini-live-2.5-flash-preview", (string?)setup["setup"]!["model"]);
        Assert.Equal("TEXT", (string?)setup["setup"]!["generationConfig"]!["responseModalities"]![0]);
        Assert.Equal("hi", (string?)setup["setup"]!["inputAudioTranscription"]!["languageCodes"]![0]);
    }

    [Fact]
    public void Setup_CustomWithoutModel_LeavesItToTheServer()
    {
        var setup = JsonNode.Parse(RealtimeProtocol.Setup(LiveService.Custom, "", null)!)!;

        Assert.Null(setup["session"]!["audio"]!["input"]!["transcription"]!["model"]);
    }

    [Fact]
    public void Setup_XAi_IsNone() => Assert.Null(RealtimeProtocol.Setup(LiveService.XAI, "grok-stt", null));

    [Theory]
    [InlineData(nameof(LiveService.Gemini), true)]
    [InlineData(nameof(LiveService.XAI), true)]
    [InlineData(nameof(LiveService.OpenAI), false)]
    [InlineData(nameof(LiveService.Deepgram), false)]
    public void NeedsReady_Service_OnlyWhereEarlyAudioIsRefused(string service, bool expected) =>
        Assert.Equal(expected, RealtimeProtocol.NeedsReady(Enum.Parse<LiveService>(service)));

    [Fact]
    public void AppendAudio_Gemini_CarriesBase64At16k()
    {
        var message = JsonNode.Parse(RealtimeProtocol.AppendAudio(LiveService.Gemini, [1, 2, 3])!)!;

        Assert.Equal("AQID", (string?)message["realtimeInput"]!["audio"]!["data"]);
        Assert.Equal("audio/pcm;rate=16000", (string?)message["realtimeInput"]!["audio"]!["mimeType"]);
    }

    [Theory]
    [InlineData(nameof(LiveService.XAI))]
    [InlineData(nameof(LiveService.Deepgram))]
    public void AppendAudio_BinaryServices_SendNoJson(string service) =>
        Assert.Null(RealtimeProtocol.AppendAudio(Enum.Parse<LiveService>(service), [1, 2, 3]));

    [Theory]
    [InlineData(nameof(LiveService.Gemini), """{"realtimeInput":{"audioStreamEnd":true}}""")]
    [InlineData(nameof(LiveService.XAI), """{"type":"audio.done"}""")]
    [InlineData(nameof(LiveService.Custom), """{"type":"input_audio_buffer.commit"}""")]
    public void Finish_Service_SaysTheAudioEnded(string service, string expected) =>
        Assert.Equal(expected, RealtimeProtocol.Finish(Enum.Parse<LiveService>(service)));

    [Theory]
    [InlineData("""{"setupComplete":{}}""", "Ready", "")]
    [InlineData("""{"serverContent":{"inputTranscription":{"text":" hello there"}}}""", "Final", "hello there")]
    [InlineData("""{"serverContent":{"interimInputTranscription":{"text":" hel"}}}""", "Partial", "hel")]
    [InlineData("""{"serverContent":{"turnComplete":true}}""", "Done", "")]
    [InlineData("""{"serverContent":{"modelTurn":{"parts":[{"text":"an answer"}]}}}""", "Other", "")]
    public void Parse_Gemini_ReadsWhatItHeard(string json, string kind, string text)
    {
        var e = RealtimeProtocol.Parse(LiveService.Gemini, json);

        Assert.Equal((Enum.Parse<RealtimeEventKind>(kind), text), (e.Kind, e.Text));
    }

    [Theory]
    [InlineData("""{"type":"transcript.created"}""", "Ready", "")]
    [InlineData("""{"type":"transcript.partial","text":"hello","is_final":false}""", "Partial", "hello")]
    [InlineData("""{"type":"transcript.partial","text":"hello","is_final":true,"speech_final":false}""",
        "Partial", "hello")]
    [InlineData("""{"type":"transcript.partial","text":"hello there","is_final":true,"speech_final":true}""",
        "Final", "hello there")]
    [InlineData("""{"type":"transcript.done","text":""}""", "Done", "")]
    [InlineData("""{"type":"error","message":"synthetic"}""", "Error", "")]
    public void Parse_XAi_ReplacesUntilFinal(string json, string kind, string text)
    {
        var e = RealtimeProtocol.Parse(LiveService.XAI, json);

        Assert.Equal((Enum.Parse<RealtimeEventKind>(kind), text), (e.Kind, e.Text));
    }

    [Fact]
    public void Text_GeminiTurnCompleteMidDictation_IsNotTheEnd()
    {
        var text = new RealtimeText();
        text.Apply(RealtimeProtocol.Parse(
            LiveService.Gemini, """{"serverContent":{"inputTranscription":{"text":"one."}}}"""));

        text.Apply(RealtimeProtocol.Parse(LiveService.Gemini, """{"serverContent":{"turnComplete":true}}"""));
        text.FinishSent();

        Assert.False(text.Complete);
    }

    [Fact]
    public void Text_XAiPartialsThenFinal_ReplaceThenSettle()
    {
        var text = new RealtimeText();
        foreach (var json in new[]
        {
            """{"type":"transcript.partial","text":"hel","is_final":false}""",
            """{"type":"transcript.partial","text":"hello the","is_final":false}""",
            """{"type":"transcript.partial","text":"hello there","is_final":true,"speech_final":true}""",
        })
        {
            text.Apply(RealtimeProtocol.Parse(LiveService.XAI, json));
        }

        text.FinishSent();
        text.Apply(RealtimeProtocol.Parse(LiveService.XAI, """{"type":"transcript.done","text":""}"""));

        Assert.True(text.Complete);
        Assert.Equal("hello there", text.Stable);
        Assert.Equal("hello there", text.Preview);
    }

    [Fact]
    public void RealtimeUrl_UserInfo_IsDropped() =>
        Assert.Equal(
            "wss://speech.example.test:8443/v1/realtime?intent=transcription",
            RealtimeProtocol.RealtimeUrl(new("https://user:secret@speech.example.test:8443/v1"))!.ToString());

    [Fact]
    public void Setup_GeminiModelWithPrefix_IsNotDoubled() =>
        Assert.Equal(
            "models/gemini-x",
            (string?)JsonNode.Parse(
                RealtimeProtocol.Setup(LiveService.Gemini, "models/gemini-x", null)!)!["setup"]!["model"]);

    [Fact]
    public void Text_GeminiStream_EndsCompleteWithEveryPiece()
    {
        var text = new RealtimeText();
        foreach (var json in new[]
        {
            """{"serverContent":{"interimInputTranscription":{"text":"hel"}}}""",
            """{"serverContent":{"inputTranscription":{"text":"hello there."}}}""",
            """{"serverContent":{"inputTranscription":{"text":"How are you?"}}}""",
        })
        {
            text.Apply(RealtimeProtocol.Parse(LiveService.Gemini, json));
        }

        text.FinishSent();
        text.Apply(RealtimeProtocol.Parse(LiveService.Gemini, """{"serverContent":{"turnComplete":true}}"""));

        Assert.True(text.Complete);
        Assert.Equal("hello there. How are you?", text.Stable);
    }

    [Fact]
    public void Setup_OpenAi_AsksForTranscriptionOf24kAudioWithServerPauses()
    {
        var setup = JsonNode.Parse(RealtimeProtocol.Setup(LiveService.OpenAI, "gpt-4o-mini-transcribe", "en")!)!;

        Assert.Equal("session.update", (string?)setup["type"]);
        var input = setup["session"]!["audio"]!["input"]!;
        Assert.Equal("transcription", (string?)setup["session"]!["type"]);
        Assert.Equal(24000, (int)input["format"]!["rate"]!);
        Assert.Equal("gpt-4o-mini-transcribe", (string?)input["transcription"]!["model"]);
        Assert.Equal("en", (string?)input["transcription"]!["language"]);
        Assert.Equal("server_vad", (string?)input["turn_detection"]!["type"]);
    }

    [Fact]
    public void Setup_Deepgram_IsNone() => Assert.Null(RealtimeProtocol.Setup(LiveService.Deepgram, "nova-3", null));

    [Fact]
    public void AppendAudio_CarriesTheBytesAsBase64()
    {
        var message = JsonNode.Parse(RealtimeProtocol.AppendAudio(LiveService.OpenAI, [1, 2, 3])!)!;

        Assert.Equal("input_audio_buffer.append", (string?)message["type"]);
        Assert.Equal("AQID", (string?)message["audio"]);
    }

    [Theory]
    [InlineData("""{"type":"input_audio_buffer.committed","item_id":"a"}""",
        nameof(RealtimeEventKind.Committed), "a", "")]
    [InlineData("""{"type":"conversation.item.input_audio_transcription.delta","item_id":"a","delta":" syn"}""",
        nameof(RealtimeEventKind.Partial), "a", " syn")]
    [InlineData(
        """{"type":"conversation.item.input_audio_transcription.completed","item_id":"a","transcript":" Hi. "}""",
        nameof(RealtimeEventKind.Final), "a", "Hi.")]
    [InlineData("""{"type":"error","error":{"code":"input_audio_buffer_commit_empty"}}""",
        nameof(RealtimeEventKind.NothingToCommit), null, "")]
    [InlineData("""{"type":"error","error":{"code":"invalid_api_key"}}""", nameof(RealtimeEventKind.Error), null, "")]
    [InlineData("""{"type":"session.updated"}""", nameof(RealtimeEventKind.Other), null, "")]
    [InlineData("not json", nameof(RealtimeEventKind.Error), null, "")]
    public void Parse_OpenAiMessages(string json, string kind, string? item, string text) =>
        Assert.Equal(
            new RealtimeEvent(Enum.Parse<RealtimeEventKind>(kind), item, text),
            RealtimeProtocol.Parse(LiveService.OpenAI, json));

    [Theory]
    [InlineData("""{"type":"Results","is_final":false,"channel":{"alternatives":[{"transcript":"syn"}]}}""",
        nameof(RealtimeEventKind.Partial), "syn")]
    [InlineData("""{"type":"Results","is_final":true,"channel":{"alternatives":[{"transcript":" Hi. "}]}}""",
        nameof(RealtimeEventKind.Final), "Hi.")]
    [InlineData("""{"type":"Metadata"}""", nameof(RealtimeEventKind.Done), "")]
    [InlineData("""{"type":"Error","description":"synthetic"}""", nameof(RealtimeEventKind.Error), "")]
    public void Parse_DeepgramMessages(string json, string kind, string text) =>
        Assert.Equal(
            new RealtimeEvent(Enum.Parse<RealtimeEventKind>(kind), null, text),
            RealtimeProtocol.Parse(LiveService.Deepgram, json));

    [Fact]
    public void To24k_MakesThreeSamplesForEveryTwo()
    {
        var input = new byte[8];
        short[] samples = [0, 300, 600, 900];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(input.AsSpan(i * 2), samples[i]);
        }

        var output = Resample.To24k(input);

        var read = Enumerable.Range(0, output.Length / 2)
            .Select(i => BinaryPrimitives.ReadInt16LittleEndian(output.AsSpan(i * 2)))
            .ToArray();
        Assert.Equal([0, 200, 400, 600, 800, 900], read);
    }
}
