namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// Backoff for one document. The poll interval is separate: a document is not claimed again before this delay.
internal static class OutboxRetryDelay
{
    public static TimeSpan ForAttempt(OutboxRelayOptions options, int attemptCount)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        var delaySeconds = (double)options.RetryInitialDelaySeconds;
        for (var attempt = 1; attempt < attemptCount; attempt++)
        {
            delaySeconds *= options.RetryMultiplier;
            if (delaySeconds >= options.RetryMaxDelaySeconds)
            {
                return TimeSpan.FromSeconds(options.RetryMaxDelaySeconds);
            }
        }

        if (delaySeconds >= options.RetryMaxDelaySeconds)
        {
            return TimeSpan.FromSeconds(options.RetryMaxDelaySeconds);
        }

        return TimeSpan.FromSeconds(delaySeconds);
    }
}
