using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxServiceCollectionTests
{
    [Fact]
    public void AddOutbox_WhenTheRelayIsOff_DoesNotRegisterTheSnsClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = Config(("OutboxRelay:Enabled", "false"));

        services.AddOutbox(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<DisabledOutboxPublisher>(provider.GetRequiredService<IOutboxPublisher>());
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAmazonSimpleNotificationService));
    }

    [Fact]
    public void AddOutbox_WhenTheRelayIsOn_RegistersTheSnsPublisher()
    {
        var services = new ServiceCollection();
        var configuration = Config(
            ("OutboxRelay:Enabled", "true"),
            ("OutboxRelay:TopicArn", "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_events"),
            ("OutboxRelay:ServiceUrl", "http://floci:4566"));

        services.AddOutbox(configuration);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAmazonSimpleNotificationService));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IOutboxPublisher)
                && descriptor.ImplementationType == typeof(SnsOutboxPublisher));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(OutboxRelay));
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
