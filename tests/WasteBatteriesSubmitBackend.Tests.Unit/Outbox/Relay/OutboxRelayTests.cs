using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using NSubstitute;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxRelayTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishPending_WhenTwoDocumentsAreClaimed_PublishesEachIdAndMarksItSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = Document("00000000-0000-0000-0000-000000000001", Now, "corr-earlier");
        var later = Document("00000000-0000-0000-0000-000000000002", Now.AddMinutes(1), "corr-later");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(earlier, later, (BsonDocument?)null);
        repository.MarkSentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, publisher, logger, retentionDays: 90);

        await relay.PublishPendingAsync(cancellationToken);

        Received.InOrder(() =>
        {
            publisher.PublishAsync(earlier, cancellationToken);
            publisher.PublishAsync(later, cancellationToken);
        });
        await repository.Received(1).MarkSentAsync(
            earlier["_id"].AsString,
            relay.ClaimedBy,
            Now,
            Now.AddDays(90),
            cancellationToken);
        await repository.Received(1).MarkSentAsync(
            later["_id"].AsString,
            relay.ClaimedBy,
            Now,
            Now.AddDays(90),
            cancellationToken);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("corr-earlier", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("corr-later", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishPending_WhenThePublishFails_LeavesTheSameIdUnsentAndSetsTheNextAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", Now, "corr-retry");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, cancellationToken)
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        repository.RecordFailureAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, publisher, logger, initialDelaySeconds: 1, multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.DidNotReceive().MarkSentAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await repository.Received(1).RecordFailureAsync(
            document["_id"].AsString,
            relay.ClaimedBy,
            1,
            Now.AddSeconds(1),
            cancellationToken);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("corr-retry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishPending_WhenAPreviousAttemptFailed_KeepsTheEventIdAndDoublesTheDelay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", Now, "corr-retry");
        document["attemptCount"] = 1;
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, cancellationToken)
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        var relay = CreateRelay(repository, publisher, new CapturingLogger<OutboxRelay>(), initialDelaySeconds: 1, multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).RecordFailureAsync(
            "00000000-0000-0000-0000-000000000003",
            relay.ClaimedBy,
            2,
            Now.AddSeconds(2),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheFirstPublishFails_StillPublishesTheNextDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failed = Document("00000000-0000-0000-0000-000000000004", Now, "corr-failed");
        var next = Document("00000000-0000-0000-0000-000000000005", Now.AddMinutes(1), "corr-next");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(failed, next, (BsonDocument?)null);
        publisher.PublishAsync(failed, cancellationToken)
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        repository.RecordFailureAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        repository.MarkSentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var relay = CreateRelay(repository, publisher, new CapturingLogger<OutboxRelay>());

        await relay.PublishPendingAsync(cancellationToken);

        await publisher.Received(1).PublishAsync(next, cancellationToken);
        await repository.Received(1).MarkSentAsync(
            next["_id"].AsString,
            relay.ClaimedBy,
            Now,
            Now.AddDays(90),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheClaimIsLostAfterPublish_DoesNotRecordAFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000006", Now, "corr-lost");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        repository.MarkSentAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, publisher, logger);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.DidNotReceive().RecordFailureAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("corr-lost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishPending_WhenTheClaimIsLostAfterFailure_LogsThatTheNextAttemptWasNotStored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000007", Now, "corr-lost-failure");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, cancellationToken)
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        repository.RecordFailureAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, publisher, logger);

        await relay.PublishPendingAsync(cancellationToken);

        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("corr-lost-failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheRelayIsOff_DoesNotClaim()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = Substitute.For<IOutboxRepository>();
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, Substitute.For<IOutboxPublisher>(), logger, enabled: false);
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopped.Cancel();

        await relay.RunAsync(stopped.Token);

        await repository.DidNotReceive().ClaimNextAsync(
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("off", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_WhenAPassFails_LogsAndWaitsForTheNextPass()
    {
        var repository = Substitute.For<IOutboxRepository>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BsonDocument?>>(_ => Task.FromException<BsonDocument?>(new InvalidOperationException("mongo down")));
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, Substitute.For<IOutboxPublisher>(), logger, pollIntervalSeconds: 60);
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = relay.RunAsync(stopped.Token);
        await WaitForLogAsync(logger, LogLevel.Error, cancellationToken);
        await stopped.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task ExecuteAsync_WhenThePassIsCancelled_StopsWithoutLoggingAFailedPass()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = Substitute.For<IOutboxRepository>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                started.TrySetResult();
                var cancellationToken = call.ArgAt<CancellationToken>(3);
                return WaitUntilCancelled(cancellationToken);
            });
        var logger = new CapturingLogger<OutboxRelay>();
        var relay = CreateRelay(repository, Substitute.For<IOutboxPublisher>(), logger, pollIntervalSeconds: 60);

        var cancellationToken = TestContext.Current.CancellationToken;
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var running = relay.RunAsync(stopped.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        await stopped.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

        Assert.DoesNotContain(logger.Entries, entry => entry.Message.Contains("pass failed", StringComparison.Ordinal));
    }

    private static async Task WaitForLogAsync(
        CapturingLogger<OutboxRelay> logger,
        LogLevel level,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!logger.Entries.Any(entry => entry.Level == level))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task<BsonDocument?> WaitUntilCancelled(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return null;
    }

    private static OutboxRelay CreateRelay(
        IOutboxRepository repository,
        IOutboxPublisher publisher,
        CapturingLogger<OutboxRelay> logger,
        bool enabled = true,
        int retentionDays = 90,
        int pollIntervalSeconds = 5,
        int initialDelaySeconds = 1,
        double multiplier = 2) =>
        new(
            () => repository,
            publisher,
            Options.Create(new OutboxRelayOptions
            {
                Enabled = enabled,
                BatchSize = 20,
                LeaseSeconds = 60,
                RetentionDays = retentionDays,
                PollIntervalSeconds = pollIntervalSeconds,
                RetryInitialDelaySeconds = initialDelaySeconds,
                RetryMaxDelaySeconds = 300,
                RetryMultiplier = multiplier
            }),
            new FixedTime(Now),
            logger);

    private static BsonDocument Document(string id, DateTime occurredAtUtc, string correlationId) => new()
    {
        { "_id", id },
        { "eventType", PomSubmissionOutboxEvent.PomSubmissionCreatedType },
        { "schemaVersion", 1 },
        { "subjectId", "scheme:2026" },
        { "version", OutboxEvents.CommitVersion(occurredAtUtc) },
        { "partitionKey", "scheme" },
        { "occurredAtUtc", occurredAtUtc },
        { "actorId", "actor-1" },
        { "correlationId", correlationId },
        { "sent", false }
    };

    private sealed class FixedTime(DateTime utcNow) : ITimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
