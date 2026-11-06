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

        // The framework's fallback is the W3C activity id, 00-{trace-id}-{span-id}-{flags}.
        // Logs store the trace-id, so that is the part a caller can search for.
        return TraceId(requestIdentifier);
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
        if (activity is null)
        {
            return null;
        }

        var traceId = activity.TraceId.ToHexString();
        return traceId == "00000000000000000000000000000000" ? null : traceId;
    }

    // version "-" trace-id "-" parent-id "-" flags. The trace-id is 32 hex characters.
    private static string TraceId(string requestIdentifier)
    {
        if (requestIdentifier.Length == 55
            && requestIdentifier[2] == '-'
            && requestIdentifier[35] == '-'
            && requestIdentifier[52] == '-')
        {
            var traceId = requestIdentifier.Substring(3, 32);
            if (IsHex(traceId))
            {
                return traceId.ToLowerInvariant();
            }
        }

        return requestIdentifier;
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            var hex = character is >= '0' and <= '9'
                || character is >= 'a' and <= 'f'
                || character is >= 'A' and <= 'F';
            if (!hex)
            {
                return false;
            }
        }

        return true;
    }
}
