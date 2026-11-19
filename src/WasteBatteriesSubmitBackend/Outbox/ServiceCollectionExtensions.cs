using Amazon;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Outbox;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        services.AddSingleton<IOutboxRepository, OutboxRepository>();
        services.AddSingleton<Func<IOutboxRepository>>(provider => () => provider.GetRequiredService<IOutboxRepository>());
        return services;
    }

    // sectionName is the relay's own settings. OutboxRelayDefault is applied first and this section replaces
    // any value it also sets. eventTypes is the set that feature writes; it is not read from configuration.
    // Call once per topic. Relays in one process share one SNS endpoint.
    public static IServiceCollection AddOutboxRelay(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        IReadOnlyCollection<string> eventTypes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        ArgumentNullException.ThrowIfNull(eventTypes);
        var relayEventTypes = eventTypes.ToArray();
        if (relayEventTypes.Length == 0 || relayEventTypes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        services.AddSingleton<IValidateOptions<OutboxRelayOptions>>(
            new OutboxRelayOptionsValidator(configuration, sectionName));
        services.AddOptions<OutboxRelayOptions>(sectionName)
            .Configure(options =>
            {
                Apply(configuration, sectionName, options);
                options.EventTypes = relayEventTypes;
            })
            .ValidateOnStart();

        var relayOptions = new OutboxRelayOptions();
        Apply(configuration, sectionName, relayOptions);
        relayOptions.EventTypes = relayEventTypes;
        if (relayOptions.Enabled)
        {
            RequireSharedEndpoint(services, relayOptions.ServiceUrl);
            services.TryAddSingleton(new OutboxSnsEndpoint(relayOptions.ServiceUrl ?? ""));
            services.TryAddSingleton<IAmazonSimpleNotificationService>(_ => CreateClient(relayOptions));
            services.AddSingleton<IHostedService>(provider => CreateRelay(provider, sectionName, publish: true));
        }
        else
        {
            services.AddSingleton<IHostedService>(provider => CreateRelay(provider, sectionName, publish: false));
        }

        return services;
    }

    private static void Apply(IConfiguration configuration, string sectionName, OutboxRelayOptions options)
    {
        configuration.GetSection(OutboxRelayDefaults.SectionName).Bind(options);
        configuration.GetSection(sectionName).Bind(options);
    }

    private static OutboxRelay CreateRelay(IServiceProvider provider, string sectionName, bool publish)
    {
        var options = Options.Create(
            provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(sectionName));
        IOutboxPublisher? publisher = null;
        if (publish)
        {
            publisher = new SnsOutboxPublisher(
                provider.GetRequiredService<IAmazonSimpleNotificationService>(),
                options);
        }

        return new OutboxRelay(
            provider.GetRequiredService<Func<IOutboxRepository>>(),
            options,
            provider.GetRequiredService<ITimeProvider>(),
            provider.GetRequiredService<ILogger<OutboxRelay>>(),
            publisher);
    }

    private static void RequireSharedEndpoint(IServiceCollection services, string serviceUrl)
    {
        var existing = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<OutboxSnsEndpoint>()
            .FirstOrDefault();
        if (existing is not null
            && !string.Equals(existing.ServiceUrl, serviceUrl ?? "", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Outbox relays in one process share one SNS endpoint. Set the same ServiceUrl on each relay, or set it once on OutboxRelayDefault.");
        }
    }

    private static AmazonSimpleNotificationServiceClient CreateClient(OutboxRelayOptions relay)
    {
        var config = new AmazonSimpleNotificationServiceConfig
        {
            // The relay also cancels the call after this long. One HTTP attempt must not outlive that.
            Timeout = TimeSpan.FromSeconds(OutboxRelayDefaults.PublishTimeoutSeconds)
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

    private sealed class OutboxSnsEndpoint(string serviceUrl)
    {
        public string ServiceUrl { get; } = serviceUrl;
    }
}
