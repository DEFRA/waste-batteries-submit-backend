using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

namespace WasteBatteriesSubmitBackend.Utils.Http;

// Gzip for a collection GET. A document under the same prefix is plain JSON.
// The list is the response that can reach the CDP payload limit.
public static class GzipResponseCompression
{
    public static IServiceCollection AddGzipResponseCompression(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Clear();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json"]);
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Optimal;
        });

        return services;
    }

    // routePrefix is the collection path. The document route is {routePrefix}/{id}, and that stays plain.
    // Routing also sends {routePrefix}/ to the list, which takes the gzip page size, so that path is compressed too.
    public static void UseGzipResponseCompression(this WebApplication app, string routePrefix)
    {
        app.UseWhen(
            context => IsCollectionGet(context, routePrefix),
            branch => branch.UseResponseCompression());
    }

    private static bool IsCollectionGet(HttpContext context, string routePrefix) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.StartsWithSegments(routePrefix, out var remaining)
        && (!remaining.HasValue || remaining == "/");

    // The list page limit uses this same decision. gzip;q=0 refuses gzip. * accepts it.
    internal static bool ClientAcceptsGzip(HttpRequest request)
    {
        double? gzipQuality = null;
        double? wildcardQuality = null;
        foreach (var encoding in request.GetTypedHeaders().AcceptEncoding)
        {
            if (encoding.Value.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                gzipQuality = encoding.Quality.GetValueOrDefault(1);
            }
            else if (encoding.Value.Equals("*", StringComparison.Ordinal))
            {
                wildcardQuality = encoding.Quality.GetValueOrDefault(1);
            }
        }

        var quality = gzipQuality ?? wildcardQuality;
        return quality is > 0;
    }
}
