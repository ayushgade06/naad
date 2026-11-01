using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Naad.Live;

namespace Naad.Tests.Live;

public sealed class RealtimeProtocolTests
{
    [Fact]
    public void Endpoint_Deepgram_AsksFor16kLinearAudioWithInterimResults()
    {
        var url = RealtimeProtocol.Endpoint(LiveService.Deepgram, "nova-3", "en").ToString();

        Assert.StartsWith("wss://api.deepgram.com/v1/listen?model=nova-3&encoding=linear16&sample_rate=16000", url);
        Assert.Contains("interim_results=true", url);
        Assert.EndsWith("language=en", url);
    }

    [Fact]
    public void Endpoint_DeepgramWithoutLanguage_DetectsIt() =>
        Assert.EndsWith("language=multi", RealtimeProtocol.Endpoint(LiveService.Deepgram, "nova-3", null).ToString());

    [Theory]
    [InlineData(nameof(LiveService.OpenAI), "Bearer synthetic-key")]
    [InlineData(nameof(LiveService.Deepgram), "Token synthetic-key")]
    public void Authorization_UsesEachServicesScheme(string service, string expected) =>
        Assert.Equal(expected, RealtimeProtocol.Authorization(Enum.Parse<LiveService>(service), "synthetic-key"));

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
        var message = JsonNode.Parse(RealtimeProtocol.AppendAudio([1, 2, 3]))!;

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
