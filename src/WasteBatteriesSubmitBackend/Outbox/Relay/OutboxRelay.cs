using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Serilog.Context;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.Utils.Correlation;
using WasteBatteriesSubmitBackend.Utils.Logging;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// Started by the generic host from AddOutbox in Program.cs. ExecuteAsync is the loop.
public sealed class OutboxRelay(
    Func<IOutboxRepository> repositoryFactory,
    IOptions<OutboxRelayOptions> options,
    ITimeProvider time,
    ILogger<OutboxRelay> logger,
    IOutboxPublisher? publisher = null) : BackgroundService
{
    // Stable for this process. Two tasks on one host still claim as different callers.
    internal string ClaimedBy { get; } = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    // Test entry to the same loop. The host does not call this. It calls ExecuteAsync.
    internal Task RunAsync(CancellationToken stoppingToken) => ExecuteAsync(stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.OutboxRelayOff();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // The host stops a disabled relay by cancelling this wait.
            }

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.OutboxRelayPassFailed(exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task PublishPendingAsync(CancellationToken cancellationToken = default)
    {
        if (publisher is null)
        {
            throw new InvalidOperationException("The outbox relay is off.");
        }

        // Resolved here, not in the constructor. A process with the relay off never opens Mongo.
        var repository = repositoryFactory();
        var relayOptions = options.Value;
        for (var index = 0; index < relayOptions.BatchSize; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var utcNow = UtcNow();
            var document = await repository.ClaimNextAsync(
                utcNow,
                utcNow.AddSeconds(relayOptions.LeaseSeconds),
                ClaimedBy,
                cancellationToken);
            if (document is null)
            {
                return;
            }

            await PublishOneAsync(publisher, repository, document, relayOptions, cancellationToken);
        }
    }

    private async Task PublishOneAsync(
        IOutboxPublisher publisher,
        IOutboxRepository repository,
        BsonDocument document,
        OutboxRelayOptions relayOptions,
        CancellationToken cancellationToken)
    {
        var eventId = OutboxMessage.EventId(document);
        var correlationId = OutboxMessage.CorrelationId(document);
        using (LogContext.PushProperty(CorrelationIds.ItemKey, correlationId))
        {
            try
            {
                logger.PublishingOutboxEvent(eventId, correlationId);
                // Cancel a slow call before the lease ends. The caller's token still means shutdown.
                using var publishTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                publishTimeout.CancelAfter(TimeSpan.FromSeconds(OutboxRelayOptions.PublishTimeoutSeconds));
                await publisher.PublishAsync(document, publishTimeout.Token);
                var sentAtUtc = UtcNow();
                var marked = await repository.MarkSentAsync(
                    eventId,
                    ClaimedBy,
                    sentAtUtc,
                    sentAtUtc.AddDays(relayOptions.RetentionDays),
                    cancellationToken);
                if (marked)
                {
                    logger.PublishedOutboxEvent(eventId, correlationId);
                }
                else
                {
                    logger.OutboxClaimLostAfterPublish(eventId, correlationId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.FailedToPublishOutboxEvent(exception, eventId, correlationId);
                var attemptCount = AttemptCount(document) + 1;
                var recorded = await repository.RecordFailureAsync(
                    eventId,
                    ClaimedBy,
                    attemptCount,
                    UtcNow().Add(GetNextAttempt(relayOptions, attemptCount)),
                    cancellationToken);
                if (!recorded)
                {
                    logger.OutboxClaimLostAfterFailure(eventId, correlationId);
                }
            }
        }
    }

    private DateTime UtcNow()
    {
        var utcNow = time.UtcNow;
        return utcNow.Kind == DateTimeKind.Utc ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    // The poll interval is separate: a document is not claimed again before this delay.
    private static TimeSpan GetNextAttempt(OutboxRelayOptions options, int attemptCount)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);

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

    private static int AttemptCount(BsonDocument document)
    {
        if (!document.TryGetValue("attemptCount", out var value) || value.IsBsonNull)
        {
            return 0;
        }

        return value.ToInt32();
    }
}
