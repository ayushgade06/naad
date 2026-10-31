using Naad.LanguageModel;

namespace Naad.Tests.LanguageModel;

public sealed class RateLimitHeadersTests
{
    [Theory]
    [InlineData("30", 30_000)]
    [InlineData("7.66s", 7_660)]
    [InlineData("120ms", 120)]
    [InlineData("2m59.56s", 179_560)]
    [InlineData("1h2m", 3_720_000)]
    public void Duration_ProviderFormats_Parse(string text, long milliseconds)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), RateLimitHeaders.Duration(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("5x")]
    [InlineData("-3")]
    public void Duration_Other_IsNull(string? text)
    {
        Assert.Null(RateLimitHeaders.Duration(text));
    }

    [Fact]
    public void Read_NoRequestsLeft_UsesTheRequestResetAsDaily()
    {
        var read = RateLimitHeaders.Read(Headers(
            ("X-RateLimit-Remaining-Requests", "0"), ("x-ratelimit-reset-requests", "5h"), ("retry-after", "2")));

        Assert.Equal((TimeSpan.FromHours(5), true), read);
    }

    [Fact]
    public void Read_RetryAfter_WinsOverTheTokenReset()
    {
        var read = RateLimitHeaders.Read(Headers(
            ("x-ratelimit-remaining-requests", "10"), ("retry-after", "2"), ("x-ratelimit-reset-tokens", "9s")));

        Assert.Equal((TimeSpan.FromSeconds(2), false), read);
    }

    [Fact]
    public void Read_NothingUseful_WaitsTheDefault()
    {
        Assert.Equal((RateLimitHeaders.DefaultWait, false), RateLimitHeaders.Read(Headers()));
    }

    private static Dictionary<string, IEnumerable<string>> Headers(params (string Name, string Value)[] headers) =>
        headers.ToDictionary(h => h.Name, h => (IEnumerable<string>)[h.Value]);
}
