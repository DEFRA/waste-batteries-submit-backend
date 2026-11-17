using Amazon;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Outbox;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<Func<IOutboxRepository>>(provider => () => provider.GetRequiredService<IOutboxRepository>());
        services.AddSingleton<IValidateOptions<OutboxRelayOptions>, OutboxRelayOptionsValidator>();
        services.AddOptions<OutboxRelayOptions>()
            .Bind(configuration.GetSection(OutboxRelayOptions.SectionName))
            .ValidateOnStart();

        var relay = configuration.GetSection(OutboxRelayOptions.SectionName).Get<OutboxRelayOptions>() ?? new OutboxRelayOptions();
        if (relay.Enabled)
        {
            services.AddSingleton<IAmazonSimpleNotificationService>(_ => CreateClient(relay));
            services.AddSingleton<IOutboxPublisher, SnsOutboxPublisher>();
            services.AddHostedService<OutboxRelay>();
        }
        else
        {
            // No SNS client. The relay still starts, logs that it is off, and does not publish.
            services.AddSingleton<IHostedService>(provider => new OutboxRelay(
                provider.GetRequiredService<Func<IOutboxRepository>>(),
                provider.GetRequiredService<IOptions<OutboxRelayOptions>>(),
                provider.GetRequiredService<ITimeProvider>(),
                provider.GetRequiredService<ILogger<OutboxRelay>>()));
        }
        return services;
    }

    private static AmazonSimpleNotificationServiceClient CreateClient(OutboxRelayOptions relay)
    {
        var config = new AmazonSimpleNotificationServiceConfig
        {
            // The relay also cancels the call after this long. One HTTP attempt must not outlive that.
            Timeout = TimeSpan.FromSeconds(OutboxRelayOptions.PublishTimeoutSeconds)
        };
        if (string.IsNullOrWhiteSpace(relay.ServiceUrl))
        {
            config.RegionEndpoint = RegionEndpoint.EUWest2;
        }
        else
        {
            // ServiceURL and RegionEndpoint cannot both be set. AuthenticationRegion keeps signing on eu-west-2.
            config.ServiceURL = relay.ServiceUrl;
            config.AuthenticationRegion = RegionEndpoint.EUWest2.SystemName;
        }

        return new AmazonSimpleNotificationServiceClient(config);
    }
}
