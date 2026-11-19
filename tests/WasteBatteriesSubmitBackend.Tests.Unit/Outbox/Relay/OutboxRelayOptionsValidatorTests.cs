using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using WasteBatteriesSubmitBackend.Outbox.Relay;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxRelayOptionsValidatorTests
{
    private const string SectionName = "SampleOutboxRelay";

    private readonly OutboxRelayOptionsValidator _validator =
        new(new ConfigurationBuilder().Build(), SectionName);

    [Fact]
    public void Validate_WhenEnabledIsLeftAtTheDefault_RejectsThePlaceholderTopic()
    {
        var result = _validator.Validate(SectionName, new OutboxRelayOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("TopicArn", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheRelayIsOff_AllowsThePlaceholderTopic()
    {
        var result = _validator.Validate(SectionName, new OutboxRelayOptions { Enabled = false });

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_WhenTheRelayIsOnWithATopic_Succeeds()
    {
        var result = _validator.Validate(SectionName, Enabled(serviceUrl: ""));

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_WhenTheServiceUrlIsAnAbsoluteHttpUrl_Succeeds()
    {
        var result = _validator.Validate(SectionName, Enabled(serviceUrl: "http://floci:4566"));

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_WhenTheRelayIsOnWithoutAnSnsArn_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions { Enabled = true, TopicArn = "set-automatically-when-deployed" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("TopicArn", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheLeaseDoesNotOutlastThePublishTimeout_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions { Enabled = false, LeaseSeconds = OutboxRelayOptions.PublishTimeoutSeconds });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("LeaseSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenThePollIntervalIsZero_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions { Enabled = false, PollIntervalSeconds = 0 });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("PollIntervalSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheRetryCapIsBelowTheInitialDelay_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions { Enabled = false, RetryInitialDelaySeconds = 10, RetryMaxDelaySeconds = 5 });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RetryMaxDelaySeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheMultiplierIsBelowOne_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions { Enabled = false, RetryMultiplier = 0.5 });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("RetryMultiplier", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheRelayIsOnWithoutEventTypes_Fails()
    {
        var result = _validator.Validate(
            SectionName,
            new OutboxRelayOptions
            {
                Enabled = true,
                TopicArn = "arn:aws:sns:eu-west-2:000000000000:events",
                EventTypes = []
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("EventTypes", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenTheServiceUrlIsNotHttp_Fails()
    {
        var result = _validator.Validate(SectionName, Enabled(serviceUrl: "floci:4566"));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("ServiceUrl", StringComparison.Ordinal));
    }

    private static OutboxRelayOptions Enabled(string serviceUrl) => new()
    {
        Enabled = true,
        TopicArn = "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_pom_submission_events",
        ServiceUrl = serviceUrl,
        EventTypes = ["SampleEvent"]
    };
}
