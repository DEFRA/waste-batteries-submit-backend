namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// Timings shared by every relay. A relay section can set the same property again to replace it.
public class OutboxRelayDefaults
{
    public const string SectionName = "OutboxRelayDefault";

    // The SNS call is cancelled after this long. The lease must be longer, so a slow publish
    // fails and the claim is released before another task can take the document.
    public const int PublishTimeoutSeconds = 30;

    public int PollIntervalSeconds { get; init; } = 5;

    public int BatchSize { get; init; } = 20;

    public int LeaseSeconds { get; init; } = 60;

    public int RetentionDays { get; init; } = 90;

    public int RetryInitialDelaySeconds { get; init; } = 1;

    public int RetryMaxDelaySeconds { get; init; } = 300;

    public double RetryMultiplier { get; init; } = 2;
}

// One SNS topic. Enabled, the topic, the endpoint and the event types belong to that relay.
// Anything else is taken from OutboxRelayDefault unless this relay sets it.
public sealed class OutboxRelayOptions : OutboxRelayDefaults
{
    public bool Enabled { get; init; } = true;

    public string TopicArn { get; init; } = "";

    // Empty in CDP, so the SDK uses the regional endpoint and the task role. Set for floci.
    public string ServiceUrl { get; init; } = "";

    // Supplied by the feature that registers the relay, from the same constants it writes.
    // Documents whose eventType is not listed stay in the outbox for another relay.
    public string[] EventTypes { get; set; } = [];
}
