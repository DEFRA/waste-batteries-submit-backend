using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Utils.Actor;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddActorContext(this IServiceCollection services)
    {
        services.AddScoped<IActorContext, ActorContext>();
        return services;
    }
}
