using System.Diagnostics;

namespace WasteBatteriesSubmitBackend.Utils.Logging;

// CDP OpenSearch stores x-cdp-request-id as trace.id, overwriting the activity trace id.
// Echo that header in the problem-details body so a deployed error can be found in the logs.
// With no header, use the activity trace id, which is the trace.id written to the local console.
internal static class ProblemDetailsTraceId
{
    public static void Apply(ProblemDetailsContext context, string? traceHeaderName)
    {
        var httpContext = context.HttpContext;
        context.ProblemDetails.Extensions["traceId"] = Select(
            ReadHeader(httpContext, traceHeaderName),
            ActivityTraceId(),
            httpContext.TraceIdentifier);
    }

    public static string Select(string? requestTraceId, string? activityTraceId, string requestIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(requestTraceId))
        {
            return requestTraceId;
        }

        if (!string.IsNullOrWhiteSpace(activityTraceId))
        {
            return activityTraceId;
        }

        return requestIdentifier;
    }

    private static string? ReadHeader(HttpContext httpContext, string? traceHeaderName)
    {
        if (string.IsNullOrWhiteSpace(traceHeaderName)
            || !httpContext.Request.Headers.TryGetValue(traceHeaderName, out var values))
        {
            return null;
        }

        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? ActivityTraceId()
    {
        var activity = Activity.Current;
        return activity?.TraceId.ToHexString();
    }
}
