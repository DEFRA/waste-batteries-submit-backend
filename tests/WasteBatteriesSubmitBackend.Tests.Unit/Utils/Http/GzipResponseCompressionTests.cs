using Microsoft.AspNetCore.Http;
using WasteBatteriesSubmitBackend.Utils.Http;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Http;

public class GzipResponseCompressionTests
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

        Assert.Equal(acceptsGzip, GzipResponseCompression.ClientAcceptsGzip(context.Request));
    }
}
