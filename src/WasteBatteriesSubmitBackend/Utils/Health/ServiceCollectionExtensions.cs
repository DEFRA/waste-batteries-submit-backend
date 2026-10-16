using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Health;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHealth(this IServiceCollection services)
    {
        services.AddHealthChecks();
        return services;
    }
}
