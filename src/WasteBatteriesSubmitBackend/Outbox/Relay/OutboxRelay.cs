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
    IOutboxPublisher publisher,
    IOptions<OutboxRelayOptions> options,
    ITimeProvider time,
    ILogger<OutboxRelay> logger) : BackgroundService
{
    // Stable for this process. Two tasks on one host still claim as different callers.
    internal string ClaimedBy { get; } = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task PublishPendingAsync(CancellationToken cancellationToken = default)
    {
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

            await PublishOneAsync(repository, document, relayOptions, cancellationToken);
        }
    }

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

    private async Task PublishOneAsync(
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
                await publisher.PublishAsync(document, cancellationToken);
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
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.FailedToPublishOutboxEvent(exception, eventId, correlationId);
                var attemptCount = AttemptCount(document) + 1;
                var recorded = await repository.RecordFailureAsync(
                    eventId,
                    ClaimedBy,
                    attemptCount,
                    UtcNow().Add(OutboxRetryDelay.ForAttempt(relayOptions, attemptCount)),
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

    private static int AttemptCount(BsonDocument document)
    {
        if (!document.TryGetValue("attemptCount", out var value) || value.IsBsonNull)
        {
            return 0;
        }

        return value.ToInt32();
    }
}
