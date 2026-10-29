using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace WasteBatteriesSubmitBackend.Utils.Health;

[ExcludeFromCodeCoverage]
public static class WebApplicationExtensions
{
    public static WebApplication MapHealth(this WebApplication app)
    {
        // TEMPORARY: Split liveness and readiness once Mongo has a real health check.
        // Keep GET /health as the live probe: CDP, the PR workflow, and ZAP call it.
        // Map /health/ready for a "ready"-tagged Mongo check, as the EPR backend does.
        // Do not split while AddHealth only calls AddHealthChecks() with no checks.
        app.MapHealthChecks("/health", new HealthCheckOptions()).AllowAnonymous();
        return app;
    }
}
