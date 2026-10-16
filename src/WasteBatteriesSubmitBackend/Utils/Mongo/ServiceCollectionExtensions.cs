using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Config;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMongo(this IServiceCollection services, IConfiguration configuration)
    {
        MongoExtensions.Register();
        MongoConventions.Register();

        services
            .AddOptions<MongoConfig>()
            .Bind(configuration.GetRequiredSection("Mongo"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IMongoDbClientFactory, MongoDbClientFactory>();

        return services;
    }
}
