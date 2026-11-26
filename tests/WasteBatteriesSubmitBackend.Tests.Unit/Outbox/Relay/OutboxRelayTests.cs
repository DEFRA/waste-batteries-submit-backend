using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using NSubstitute;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxRelayTests
{
    private static readonly DateTime s_now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

    private static readonly string[] s_sampleEventTypes = ["SampleEvent"];

    [Fact]
    public async Task PublishPending_WhenTwoDocumentsAreClaimed_PublishesEachIdAndMarksItSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlier = Document("00000000-0000-0000-0000-000000000001", s_now, "corr-earlier");
        var later = Document("00000000-0000-0000-0000-000000000002", s_now.AddMinutes(1), "corr-later");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
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
            publisher.PublishAsync(earlier, Arg.Any<CancellationToken>());
            publisher.PublishAsync(later, Arg.Any<CancellationToken>());
        });
        await repository.Received(1).MarkSentAsync(
            earlier["_id"].AsString,
            relay.ClaimedBy,
            s_now,
            s_now.AddDays(90),
            cancellationToken);
        await repository.Received(1).MarkSentAsync(
            later["_id"].AsString,
            relay.ClaimedBy,
            s_now,
            s_now.AddDays(90),
            cancellationToken);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("corr-earlier", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("corr-later", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishPending_WhenThePublishFails_LeavesTheSameIdUnsentAndSetsTheNextAttempt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", s_now, "corr-retry");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
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
            s_now.AddSeconds(1),
            cancellationToken);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Error && entry.Message.Contains("corr-retry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishPending_WhenAPreviousAttemptFailed_KeepsTheEventIdAndDoublesTheDelay()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", s_now, "corr-retry");
        document["attemptCount"] = 1;
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        var relay = CreateRelay(repository, publisher, new CapturingLogger<OutboxRelay>(), initialDelaySeconds: 1, multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).RecordFailureAsync(
            "00000000-0000-0000-0000-000000000003",
            relay.ClaimedBy,
            2,
            s_now.AddSeconds(2),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheDelayWouldPassTheCap_StopsAtTheCap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", s_now, "corr-retry");
        document["attemptCount"] = 9;
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        var relay = CreateRelay(
            repository,
            publisher,
            new CapturingLogger<OutboxRelay>(),
            initialDelaySeconds: 1,
            maxDelaySeconds: 300,
            multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).RecordFailureAsync(
            document["_id"].AsString,
            relay.ClaimedBy,
            10,
            s_now.AddSeconds(300),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheInitialDelayIsAlreadyAboveTheCap_UsesTheCap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000003", s_now, "corr-retry");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("sns down"));
        var relay = CreateRelay(
            repository,
            publisher,
            new CapturingLogger<OutboxRelay>(),
            initialDelaySeconds: 10,
            maxDelaySeconds: 5,
            multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).RecordFailureAsync(
            document["_id"].AsString,
            relay.ClaimedBy,
            1,
            s_now.AddSeconds(5),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheFirstPublishFails_StillPublishesTheNextDocument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var failed = Document("00000000-0000-0000-0000-000000000004", s_now, "corr-failed");
        var next = Document("00000000-0000-0000-0000-000000000005", s_now.AddMinutes(1), "corr-next");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(failed, next, (BsonDocument?)null);
        publisher.PublishAsync(failed, Arg.Any<CancellationToken>())
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

        await publisher.Received(1).PublishAsync(next, Arg.Any<CancellationToken>());
        await repository.Received(1).MarkSentAsync(
            next["_id"].AsString,
            relay.ClaimedBy,
            s_now,
            s_now.AddDays(90),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheClaimIsLostAfterPublish_DoesNotRecordAFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000006", s_now, "corr-lost");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
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
        var document = Document("00000000-0000-0000-0000-000000000007", s_now, "corr-lost-failure");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
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
    public async Task PublishPending_WhenThePublishIsCancelledIndependently_RecordsTheFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var document = Document("00000000-0000-0000-0000-000000000008", s_now, "corr-timeout");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TaskCanceledException());
        repository.RecordFailureAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var relay = CreateRelay(repository, publisher, new CapturingLogger<OutboxRelay>(), initialDelaySeconds: 1, multiplier: 2);

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).RecordFailureAsync(
            document["_id"].AsString,
            relay.ClaimedBy,
            1,
            s_now.AddSeconds(1),
            cancellationToken);
    }

    [Fact]
    public async Task PublishPending_WhenTheCallerCancelsDuringPublish_DoesNotRecordAFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var document = Document("00000000-0000-0000-0000-000000000009", s_now, "corr-shutdown");
        var repository = Substitute.For<IOutboxRepository>();
        var publisher = Substitute.For<IOutboxPublisher>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(document, (BsonDocument?)null);
        publisher.PublishAsync(document, Arg.Any<CancellationToken>())
            .Returns<Task>(_ =>
            {
                caller.Cancel();
                throw new OperationCanceledException(caller.Token);
            });
        var relay = CreateRelay(repository, publisher, new CapturingLogger<OutboxRelay>());

        await Assert.ThrowsAsync<OperationCanceledException>(() => relay.PublishPendingAsync(caller.Token));

        await repository.DidNotReceive().RecordFailureAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
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
            Arg.Any<IReadOnlyCollection<string>>(),
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
                Arg.Any<IReadOnlyCollection<string>>(),
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
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                started.TrySetResult();
                var cancellationToken = call.ArgAt<CancellationToken>(4);
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

    [Fact]
    public async Task PublishPending_WhenEventTypesAreSet_ClaimsOnlyThoseTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repository = Substitute.For<IOutboxRepository>();
        repository.ClaimNextAsync(
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns((BsonDocument?)null);
        var relay = CreateRelay(repository, Substitute.For<IOutboxPublisher>(), new CapturingLogger<OutboxRelay>());

        await relay.PublishPendingAsync(cancellationToken);

        await repository.Received(1).ClaimNextAsync(
            s_now,
            s_now.AddSeconds(60),
            relay.ClaimedBy,
            Arg.Is<IReadOnlyCollection<string>>(types => types.SequenceEqual(s_sampleEventTypes)),
            cancellationToken);
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
        int maxDelaySeconds = 300,
        double multiplier = 2) =>
        new(
            () => repository,
            Options.Create(new OutboxRelayOptions
            {
                Enabled = enabled,
                BatchSize = 20,
                LeaseSeconds = 60,
                RetentionDays = retentionDays,
                PollIntervalSeconds = pollIntervalSeconds,
                RetryInitialDelaySeconds = initialDelaySeconds,
                RetryMaxDelaySeconds = maxDelaySeconds,
                RetryMultiplier = multiplier,
                EventTypes = s_sampleEventTypes
            }),
            new FixedTime(s_now),
            logger,
            publisher);

    private static BsonDocument Document(string id, DateTime occurredAtUtc, string correlationId) => new()
    {
        { "_id", id },
        { "eventType", "SampleEvent" },
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
