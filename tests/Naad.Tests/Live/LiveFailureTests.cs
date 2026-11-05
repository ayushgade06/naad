using Naad.Live;

namespace Naad.Tests.Live;

public sealed class LiveFailureTests
{
    [Theory]
    [InlineData(401, false, false, true, "key refused")]
    [InlineData(403, true, true, true, "key refused")]
    [InlineData(429, false, false, true, "HTTP 429")]
    [InlineData(0, false, false, true, "no connection")]
    [InlineData(101, true, true, true, "connection lost")]
    [InlineData(101, true, false, true, "service error")]
    [InlineData(101, true, false, false, "no reply")]
    public void Describe_NamesTheFirstThingThatWentWrong(
        int status, bool connected, bool dropped, bool serverError, string expected) =>
        Assert.Equal(expected, LiveFailure.Describe(status, connected, dropped, serverError));
}
