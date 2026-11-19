using Amazon;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxServiceCollectionTests
{
    private const string SectionName = "SampleOutboxRelay";

    [Fact]
    public void AddOutboxRelay_WhenTheRelayIsOff_DoesNotRegisterTheSnsClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = Config(($"{SectionName}:Enabled", "false"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["SampleEvent"]);
        services.AddSingleton(Substitute.For<ITimeProvider>());
        using var provider = services.BuildServiceProvider();

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAmazonSimpleNotificationService));
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is OutboxRelay);
        Assert.False(provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(SectionName).Enabled);
    }

    [Fact]
    public void AddOutboxRelay_WhenTheRelayIsOn_RegistersTheSnsPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITimeProvider>());
        var configuration = Config(
            ($"{SectionName}:Enabled", "true"),
            ($"{SectionName}:TopicArn", "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_pom_submission_events"),
            ($"{SectionName}:ServiceUrl", "http://floci:4566"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["SampleEvent"]);
        using var provider = services.BuildServiceProvider();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAmazonSimpleNotificationService));
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is OutboxRelay);
        var client = Assert.IsType<AmazonSimpleNotificationServiceClient>(
            provider.GetRequiredService<IAmazonSimpleNotificationService>());
        Assert.Equal(TimeSpan.FromSeconds(OutboxRelayDefaults.PublishTimeoutSeconds), client.Config.Timeout);
        Assert.Equal("http://floci:4566/", client.Config.ServiceURL);
        Assert.Equal(RegionEndpoint.EUWest2.SystemName, client.Config.AuthenticationRegion);
    }

    [Fact]
    public void AddOutboxRelay_WhenTheRelayIsOnWithoutAnSnsTopic_RejectsTheOptions()
    {
        var services = new ServiceCollection();
        var configuration = Config(
            ($"{SectionName}:Enabled", "true"),
            ($"{SectionName}:TopicArn", "set-automatically-when-deployed"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["SampleEvent"]);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(SectionName));

        Assert.Contains($"{SectionName}:TopicArn", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddOutboxRelay_WhenTheRelaySetsADefault_UsesTheRelayValue()
    {
        var services = new ServiceCollection();
        var configuration = Config(
            ($"{OutboxRelayDefaults.SectionName}:LeaseSeconds", "90"),
            ($"{OutboxRelayDefaults.SectionName}:PollIntervalSeconds", "5"),
            ($"{SectionName}:Enabled", "false"),
            ($"{SectionName}:LeaseSeconds", "120"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["SampleEvent"]);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(SectionName);

        Assert.Equal(120, options.LeaseSeconds);
        Assert.Equal(5, options.PollIntervalSeconds);
        Assert.False(options.Enabled);
    }

    [Fact]
    public void AddOutboxRelay_WhenConfigurationListsEventTypes_UsesTheTypesFromCode()
    {
        var services = new ServiceCollection();
        var configuration = Config(
            ($"{SectionName}:Enabled", "false"),
            ($"{SectionName}:EventTypes:0", "FromConfiguration"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["FromCode"]);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(SectionName);

        Assert.Equal(["FromCode"], options.EventTypes);
    }

    [Fact]
    public void AddOutboxRelay_WhenOnlyTheDefaultIsInvalid_NamesTheDefaultSection()
    {
        var services = new ServiceCollection();
        var configuration = Config(
            ($"{OutboxRelayDefaults.SectionName}:PollIntervalSeconds", "0"),
            ($"{SectionName}:Enabled", "false"));

        services.AddOutbox();
        services.AddOutboxRelay(configuration, SectionName, ["SampleEvent"]);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            _ = provider.GetRequiredService<IOptionsMonitor<OutboxRelayOptions>>().Get(SectionName));

        Assert.Contains($"{OutboxRelayDefaults.SectionName}:PollIntervalSeconds", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
