using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NSubstitute;
using WasteBatteriesSubmitBackend.Config;
using WasteBatteriesSubmitBackend.Models;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Filters;
using WasteBatteriesSubmitBackend.PomSubmission.Models.Requests;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Services;
using WasteBatteriesSubmitBackend.PomSubmission.Validators;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Actor;
using WasteBatteriesSubmitBackend.Utils.Correlation;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.PomSubmission.Services;

public partial class PomSubmissionServiceTests
{
    private const int MaxPageSize = 25;

    private const int MaxGzipPageSize = 40;

    private const string TestCorrelationId = "test-correlation-id";

    private const string TestActorId = "actor-1";

    private static readonly DateTime s_createdAt = SamplePomSubmission.CreatedAt;

    private static void ReturnNoSubmissions(IPomSubmissionRepository repository) =>
        repository
            .FindAsync(Arg.Any<PomSubmissionFilter>(), Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaginatedList<PomSubmissionModel>([], 0, 1, 0));

    private static readonly IOptions<PomSubmissionConfig> s_config = Options.Create(new PomSubmissionConfig
    {
        InitialStatus = "Submitted",
        InitialVersion = 1,
        InitialActivity = "Placed",
        InitialBatteryCategory = "Portable",
        MaxPageSize = MaxPageSize,
        MaxGzipPageSize = MaxGzipPageSize
    });

    private static PomSubmissionService Service(
        IPomSubmissionRepository repository,
        CapturingLogger<PomSubmissionService> logger,
        IUnitOfWork? unitOfWork = null,
        IOutboxRepository? outbox = null,
        ICorrelationContext? correlation = null,
        IActorContext? actor = null) =>
        new(
            new PomSubmissionDependencies(
                repository,
                outbox ?? Substitute.For<IOutboxRepository>(),
                unitOfWork ?? Substitute.For<IUnitOfWork>(),
                new PomSubmissionMapper(s_config),
                new PomSubmissionValidators(
                    new PomSubmissionRequestValidator(),
                    new PomSubmissionFilterRequestValidator()),
                s_config),
            new FixedTime(s_createdAt),
            correlation ?? new TestCorrelationContext(TestCorrelationId),
            actor ?? new TestActorContext(TestActorId),
            logger);

    private static IUnitOfWork TransactionFor<T>(IClientSessionHandle session)
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var transaction = Substitute.For<IMongoTransaction>();
        transaction.Session.Returns(session);
        unitOfWork
            .ExecuteAsync(
                Arg.Any<Func<IMongoTransaction, CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var work = call.Arg<Func<IMongoTransaction, CancellationToken, Task<T>>>();
                return work(transaction, call.Arg<CancellationToken>());
            });
        return unitOfWork;
    }

    private static async Task AssertSingleOutboxEvent(
        IOutboxRepository outbox,
        IClientSessionHandle session,
        string schemeOrgId,
        int year,
        string eventType)
    {
        await outbox.Received(1).AddAsync(
            session,
            Arg.Is<IReadOnlyList<OutboxEvent>>(events =>
                events.Count == 1
                && events[0].EventType == eventType
                && events[0].SchemaVersion == OutboxEvent.CurrentSchemaVersion
                && events[0].SubjectId == schemeOrgId + ":" + year
                && events[0].Version == OutboxEvents.CommitVersion(s_createdAt)
                && events[0].PartitionKey == schemeOrgId
                && ((PomSubmissionOutboxEvent)events[0]).SchemeOrgId == schemeOrgId
                && ((PomSubmissionOutboxEvent)events[0]).CompliancePeriodYear == year
                && events[0].OccurredAtUtc == s_createdAt
                && events[0].ActorId == TestActorId
                && events[0].CorrelationId == TestCorrelationId
                && !events[0].Sent
                && events[0].SentAtUtc == null
                && events[0].ExpiresAtUtc == null
                && events[0].Id.Length == 36),
            Arg.Any<CancellationToken>());
    }

    private static void AssertSaveError(CapturingLogger<PomSubmissionService> logger, Exception exception)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(7, entry.EventId.Id);
        Assert.Equal("Failed to save POM submissions and outbox events", entry.Message);
        Assert.Same(exception, entry.Exception);
    }

    private sealed class TestCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; } = correlationId;
    }

    private sealed class TestActorContext(string actorId) : IActorContext
    {
        public string ActorId { get; } = actorId;
    }

    private static PomSubmissionRequest ValidRequest() => SamplePomSubmission.Request();
}
