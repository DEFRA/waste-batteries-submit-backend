using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;
using WasteBatteriesSubmitBackend.PomSubmission.Endpoints;

namespace WasteBatteriesSubmitBackend.PomSubmission;

// Gzip for the POM submission reads. The list body is the response that can reach the CDP payload limit.
[ExcludeFromCodeCoverage]
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
        app.UseWhen(IsPomSubmissionGet, branch => branch.UseResponseCompression());
    }

    private static bool IsPomSubmissionGet(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.Request.Path.StartsWithSegments(PomSubmissionEndpoints.RoutePrefix);

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
