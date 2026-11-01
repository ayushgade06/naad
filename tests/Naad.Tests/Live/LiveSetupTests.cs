using Naad.Live;
using Naad.Providers;
using Naad.Settings;

namespace Naad.Tests.Live;

public sealed class LiveSetupTests
{
    private const string FakeKey = "fake-api-key-0123456789";

    [Theory]
    [InlineData(nameof(LiveService.OpenAI), "wss://api.openai.com/v1/realtime?intent=transcription")]
    [InlineData(nameof(LiveService.XAI), "wss://api.x.ai/v1/stt?model=grok-stt")]
    [InlineData(nameof(LiveService.Gemini), "wss://generativelanguage.googleapis.com/")]
    public void Stream_NamedServiceWithKey_GoesToItsSocket(string service, string expected)
    {
        var stream = LiveSetup.Stream(Live(Enum.Parse<LiveService>(service)), _ => FakeKey)!;

        Assert.StartsWith(expected, stream.Endpoint.ToString(), StringComparison.Ordinal);
        Assert.Equal(FakeKey, stream.Key);
    }

    [Fact]
    public void Stream_NamedServiceWithoutKey_IsNone() =>
        Assert.Null(LiveSetup.Stream(Live(LiveService.Gemini), _ => null));

    [Fact]
    public void Stream_CustomWithoutKey_UsesTheCustomBaseUrl()
    {
        var s = Live(LiveService.Custom) with { TranscriptionBaseUrl = new("http://localhost:8000/v1") };

        var stream = LiveSetup.Stream(s, _ => null)!;

        Assert.Equal("ws://localhost:8000/v1/realtime?intent=transcription", stream.Endpoint.ToString());
        Assert.Null(stream.Key);
        Assert.Equal("", stream.Model);
    }

    [Fact]
    public void Stream_ChosenModel_WinsOverTheDefault() =>
        Assert.Equal(
            "nova-2",
            LiveSetup.Stream(Live(LiveService.Deepgram) with { LiveModel = "nova-2" }, _ => FakeKey)!.Model);

    [Fact]
    public void Stream_ThisPc_IsNone() => Assert.Null(LiveSetup.Stream(Live(LiveService.Local), _ => FakeKey));

    [Theory]
    [InlineData(nameof(LiveService.Gemini), nameof(CloudService.Gemini))]
    [InlineData(nameof(LiveService.Custom), nameof(CloudService.Custom))]
    public void ServiceFor_Live_UsesThatServicesKey(string live, string service) =>
        Assert.Equal(Enum.Parse<CloudService>(service), LiveSetup.ServiceFor(Enum.Parse<LiveService>(live)));

    [Theory]
    [InlineData(nameof(LiveService.Local), false, true)]
    [InlineData(nameof(LiveService.Local), null, false)]
    [InlineData(nameof(LiveService.Local), true, false)]
    [InlineData(nameof(LiveService.OpenAI), false, false)]
    public void PreviewBlocked_NoGraphicsCardOnThisPc_SaysWhy(string service, bool? onGraphicsCard, bool blocked) =>
        Assert.Equal(blocked, LivePanel.PreviewBlocked(Enum.Parse<LiveService>(service), onGraphicsCard) is not null);

    private static AppSettings Live(LiveService service) => AppSettings.Defaults with { LiveService = service };
}
