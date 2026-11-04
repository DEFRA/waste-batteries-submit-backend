using Microsoft.AspNetCore.Http;
using WasteBatteriesSubmitBackend.PomSubmission;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission;

public class PomSubmissionResponseCompressionTests
{
    [Theory]
    [InlineData("gzip", true)]
    [InlineData("gzip, deflate", true)]
    [InlineData("*", true)]
    [InlineData("br, *", true)]
    [InlineData("identity", false)]
    [InlineData("gzip;q=0", false)]
    [InlineData("gzip;q=0, *", false)]
    [InlineData("", false)]
    public void ClientAcceptsGzip_MatchesTheEncodingsTheListLimitUses(string header, bool acceptsGzip)
    {
        var context = new DefaultHttpContext();
        if (header != string.Empty)
        {
            context.Request.Headers.AcceptEncoding = header;
        }

        Assert.Equal(acceptsGzip, PomSubmissionResponseCompression.ClientAcceptsGzip(context.Request));
    }
}
