using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using WasteBatteriesSubmitBackend.PomSubmission.Endpoints;

namespace WasteBatteriesSubmitBackend.PomSubmission;

// Gzip for the POM submission list. A single submission is plain JSON. The list is the response that can reach the CDP payload limit.
public static class PomSubmissionResponseCompression
{
    public static IServiceCollection AddPomSubmissionResponseCompression(this IServiceCollection services)
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

    public static void UsePomSubmissionResponseCompression(this WebApplication app)
    {
        app.UseWhen(IsPomSubmissionList, branch => branch.UseResponseCompression());
    }

    // The document route is /pom-submissions/{id}. Only the collection path is compressed.
    // Routing also sends /pom-submissions/ to the list, which takes the gzip page size, so it must be compressed too.
    private static bool IsPomSubmissionList(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.StartsWithSegments(PomSubmissionEndpoints.RoutePrefix, out var remaining)
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
