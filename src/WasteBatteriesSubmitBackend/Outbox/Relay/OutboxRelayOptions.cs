namespace WasteBatteriesSubmitBackend.Outbox.Relay;

public sealed class OutboxRelayOptions
{
    public const string SectionName = "OutboxRelay";

    // The SNS call is cancelled after this long. The lease must be longer, so a slow publish
    // fails and the claim is released before another task can take the document.
    public const int PublishTimeoutSeconds = 30;

    // The hosted loop publishes only when this is true. Set false to leave events in the outbox.
    public bool Enabled { get; init; } = true;

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
