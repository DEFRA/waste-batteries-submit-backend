using Serilog.Context;

namespace WasteBatteriesSubmitBackend.Utils.Correlation;

// Runs before header propagation and request logging. A missing trace header is filled in
// here, so logs, later outgoing requests, and the outbox all use the same id.
public sealed class CorrelationMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headerName = configuration.GetValue<string>("TraceHeader");
        var correlationId = CorrelationIds.Establish(context, headerName);
        using (LogContext.PushProperty(CorrelationIds.ItemKey, correlationId))
        {
            await next(context);
        }
    }
}
