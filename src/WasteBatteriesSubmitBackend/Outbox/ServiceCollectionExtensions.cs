using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Outbox.Repositories;

namespace WasteBatteriesSubmitBackend.Outbox;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        return services;
    }
}
