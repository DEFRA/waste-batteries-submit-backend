using System.Diagnostics.CodeAnalysis;
using System.Net;
using WasteBatteriesSubmitBackend.Utils.Logging;

namespace WasteBatteriesSubmitBackend.Utils.Http;

[ExcludeFromCodeCoverage]
public class ProxyHttpMessageHandler : HttpClientHandler
{
    [ExcludeFromCodeCoverage]
    public ProxyHttpMessageHandler(ILogger<ProxyHttpMessageHandler> logger)
    {
        var proxyUri = Environment.GetEnvironmentVariable("HTTP_PROXY");
        var proxy = new WebProxy { BypassProxyOnLocal = true };
        if (proxyUri != null)
        {
            logger.CreatingProxyHttpClient();
            var uri = new UriBuilder(proxyUri).Uri;
            proxy.Address = uri;
        }
        else
        {
            logger.ProxyNotConfigured();
        }

        Proxy = proxy;
        UseProxy = proxyUri != null;
    }
}