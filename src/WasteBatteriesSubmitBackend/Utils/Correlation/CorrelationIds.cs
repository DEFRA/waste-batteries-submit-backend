using System.Diagnostics;

namespace WasteBatteriesSubmitBackend.Utils.Correlation;

// The id that ties this request's logs, outgoing calls, and outbox events together.
// The trace header wins when the caller sent one. Otherwise the current activity trace id
// is used, and a new id is created only when there is no activity either.
// The chosen id is stored on the request so header propagation and the log enricher see it.
public static class CorrelationIds
{
    public const string ItemKey = "CorrelationId";

    public static string Establish(HttpContext httpContext, string? headerName)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var header = ReadHeader(httpContext, headerName);
        var correlationId = header ?? ActivityTraceId() ?? Guid.NewGuid().ToString("N");
        if (header is null && !string.IsNullOrWhiteSpace(headerName))
        {
            httpContext.Request.Headers[headerName] = correlationId;
        }

        httpContext.Items[ItemKey] = correlationId;
        return correlationId;
    }

    private static string? ReadHeader(HttpContext httpContext, string? headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName)
            || !httpContext.Request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }

        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ActivityTraceId()
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return null;
        }

        var traceId = activity.TraceId.ToHexString();
        return traceId == "00000000000000000000000000000000" ? null : traceId;
    }
}
