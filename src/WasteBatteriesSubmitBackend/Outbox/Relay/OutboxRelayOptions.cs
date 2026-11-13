namespace WasteBatteriesSubmitBackend.Outbox.Relay;

public sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";

    // The hosted loop publishes only when this is true. A deploy with no topic yet leaves it false.
    public bool Enabled { get; init; }

    public string TopicArn { get; init; } = "";

    // Empty in CDP, so the SDK uses the regional endpoint and the task role. Set for floci.
    public string ServiceUrl { get; init; } = "";

    public int PollIntervalSeconds { get; init; } = 5;

    public int BatchSize { get; init; } = 20;

    public int LeaseSeconds { get; init; } = 60;

    public int RetentionDays { get; init; } = 90;

    public int RetryInitialDelaySeconds { get; init; } = 1;

    public int RetryMaxDelaySeconds { get; init; } = 300;

    public double RetryMultiplier { get; init; } = 2;
}
