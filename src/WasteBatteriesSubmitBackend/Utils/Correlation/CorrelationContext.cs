namespace WasteBatteriesSubmitBackend.Utils.Correlation;

// The correlation id for the current request. One scope returns the same id.
public interface ICorrelationContext
{
    string CorrelationId { get; }
}

public sealed class CorrelationContext(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    : ICorrelationContext
{
    private readonly string _correlationId = Resolve(
        httpContextAccessor.HttpContext,
        configuration.GetValue<string>("TraceHeader"));

    public string CorrelationId => _correlationId;

    // Middleware establishes the id first. A caller outside that middleware still gets one stable id.
    private static string Resolve(HttpContext? httpContext, string? headerName)
    {
        if (httpContext is null)
        {
            return Guid.NewGuid().ToString("N");
        }

        if (httpContext.Items.TryGetValue(CorrelationIds.ItemKey, out var stored)
            && stored is string existing
            && !string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        return CorrelationIds.Establish(httpContext, headerName);
    }
}
