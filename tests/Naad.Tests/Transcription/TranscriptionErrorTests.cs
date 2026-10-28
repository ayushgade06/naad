using System.Net;
using Naad.Transcription;

namespace Naad.Tests.Transcription;

public sealed class TranscriptionErrorTests
{
    [Theory]
    [InlineData(400, "Rejected")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Unauthorized")]
    [InlineData(404, "Rejected")]
    [InlineData(408, "Rejected")]
    [InlineData(413, "TooLarge")]
    [InlineData(415, "Rejected")]
    [InlineData(429, "RateLimited")]
    [InlineData(500, "ServerError")]
    [InlineData(504, "ServerError")]
    [InlineData(599, "ServerError")]
    public void FromStatus_EachStatus_MapsToErrorAndKeepsCode(int status, string expected)
    {
        var failure = TranscriptionFailure.FromStatus((HttpStatusCode)status);

        Assert.Equal(expected, failure.Error.ToString());
        Assert.Equal(status, failure.Code);
    }
}
