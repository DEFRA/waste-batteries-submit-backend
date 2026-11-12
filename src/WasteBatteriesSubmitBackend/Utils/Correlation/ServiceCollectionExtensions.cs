using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Correlation;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        return services;
    }

    public static void UseCorrelationId(this WebApplication app) =>
        app.UseMiddleware<CorrelationMiddleware>();
}
