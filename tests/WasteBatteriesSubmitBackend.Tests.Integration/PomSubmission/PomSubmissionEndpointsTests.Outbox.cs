using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using System.Net;
using System.Text.RegularExpressions;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Mapping;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.PomSubmission.Repositories;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Tests.Integration.Utils;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task Create_WhenTheTraceHeaderIsPresent_WritesOneUnsentOutboxEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemeOrgId = ObjectId.GenerateNewId().ToString();
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var correlationId = "create-" + Guid.NewGuid().ToString("N");
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);

        try
        {
            var response = await client.PostAsync(
                "/pom-submissions",
                Json(SubmissionBody(schemeOrgId, memberOrgId, SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            AssertUnsentEvent(
                stored,
                schemeOrgId,
                SamplePomSubmission.CompliancePeriodYear,
                correlationId,
                PomSubmissionOutboxEvent.PomSubmissionCreatedType);
        }
        finally
        {
            await DeleteSaved(schemeOrgId, correlationId, cancellationToken);
        }
    }

    [Fact]
    public async Task Create_WhenTheTraceHeaderIsAbsent_StoresACorrelationId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemeOrgId = ObjectId.GenerateNewId().ToString();
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        using var client = fixture.CreateClient();

        try
        {
            var response = await client.PostAsync(
                "/pom-submissions",
                Json(SubmissionBody(schemeOrgId, memberOrgId, SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stored = Assert.Single(await EventsForScheme(schemeOrgId, cancellationToken));
            Assert.Matches(CorrelationIdPattern(), stored["correlationId"].AsString);
            AssertUnsentEvent(
                stored,
                schemeOrgId,
                SamplePomSubmission.CompliancePeriodYear,
                stored["correlationId"].AsString,
                PomSubmissionOutboxEvent.PomSubmissionCreatedType);
        }
        finally
        {
            await DeleteScheme(schemeOrgId, cancellationToken);
        }
    }

    [Fact]
    public async Task Amend_WhenTheSubmissionIsAmended_WritesOneUnsentOutboxEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var correlationId = "amend-" + Guid.NewGuid().ToString("N");
        using var client = fixture.CreateClient();
        await CreateSubmission(
            client,
            PomSubmissionApiFixture.SchemeOrgId,
            memberOrgId,
            SamplePomSubmission.CompliancePeriodYear,
            SamplePomSubmission.CompliancePeriodQuarter,
            cancellationToken);
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);

        try
        {
            var response = await client.PatchAsync(
                "/pom-submissions",
                Json(SubmissionBody(
                    PomSubmissionApiFixture.SchemeOrgId,
                    memberOrgId,
                    SamplePomSubmission.CompliancePeriodYear,
                    SamplePomSubmission.CompliancePeriodQuarter,
                    tonnes: "3.000")),
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            AssertUnsentEvent(
                stored,
                PomSubmissionApiFixture.SchemeOrgId,
                SamplePomSubmission.CompliancePeriodYear,
                correlationId,
                PomSubmissionOutboxEvent.PomSubmissionAmendedType,
                PomSubmissionApiFixture.CreatedAt.AddMilliseconds(1));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    [Fact]
    public async Task UpsertBatch_WhenSchemesOrYearsDiffer_WritesOneEventPerPair()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemeA = ObjectId.GenerateNewId().ToString();
        var schemeB = ObjectId.GenerateNewId().ToString();
        var member1 = ObjectId.GenerateNewId().ToString();
        var member2 = ObjectId.GenerateNewId().ToString();
        var correlationId = "batch-" + Guid.NewGuid().ToString("N");
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);
        var body = BatchBody(
            SubmissionBody(schemeA, member1, 2024, "Quarter 1"),
            SubmissionBody(schemeA, member2, 2024, "Quarter 2"),
            SubmissionBody(schemeA, member1, 2025, "Quarter 1"),
            SubmissionBody(schemeB, member1, 2024, "Quarter 1"));

        try
        {
            var response = await client.PostAsync("/pom-submissions/upsert-batch", Json(body), cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var events = await EventsFor(correlationId, cancellationToken);
            Assert.Equal(3, events.Count);
            var bySubject = events.ToDictionary(outboxEvent => outboxEvent["subjectId"].AsString);
            AssertUnsentEvent(
                bySubject[SubjectId(schemeA, 2024)],
                schemeA,
                2024,
                correlationId,
                PomSubmissionOutboxEvent.PomSubmissionCreatedType);
            AssertUnsentEvent(
                bySubject[SubjectId(schemeA, 2025)],
                schemeA,
                2025,
                correlationId,
                PomSubmissionOutboxEvent.PomSubmissionCreatedType);
            AssertUnsentEvent(
                bySubject[SubjectId(schemeB, 2024)],
                schemeB,
                2024,
                correlationId,
                PomSubmissionOutboxEvent.PomSubmissionCreatedType);
            Assert.Equal(3, events.Select(outboxEvent => outboxEvent["_id"].AsString).Distinct().Count());
            Assert.Single(events.Select(outboxEvent => outboxEvent["version"].AsInt64).Distinct());
        }
        finally
        {
            await DeleteScheme(schemeA, cancellationToken);
            await DeleteScheme(schemeB, cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    [Fact]
    public async Task Create_WhenTheOutboxInsertIsRejected_RollsBackTheSubmissionAndLogsTheError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemeOrgId = ObjectId.GenerateNewId().ToString();
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var correlationId = "rollback-" + Guid.NewGuid().ToString("N");
        var errorsBefore = fixture.Logger.Entries.Count(entry => entry.EventId.Id == 7);
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);

        try
        {
            await RejectOutboxInserts(cancellationToken);
            var response = await client.PostAsync(
                "/pom-submissions",
                Json(SubmissionBody(schemeOrgId, memberOrgId, SamplePomSubmission.CompliancePeriodYear, SamplePomSubmission.CompliancePeriodQuarter)),
                cancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Empty(await EventsFor(correlationId, cancellationToken));
            Assert.Empty(await fixture.Submissions.Find(SchemeFilter(schemeOrgId)).ToListAsync(cancellationToken));
            Assert.Equal(errorsBefore + 1, fixture.Logger.Entries.Count(entry => entry.EventId.Id == 7));
            var error = fixture.Logger.Entries.Last(entry => entry.EventId.Id == 7);
            Assert.Equal(LogLevel.Error, error.Level);
            Assert.Equal("Failed to save POM submissions and outbox events", error.Message);
            Assert.NotNull(error.Exception);
        }
        finally
        {
            await AllowOutboxInserts(cancellationToken);
            await DeleteSaved(schemeOrgId, correlationId, cancellationToken);
        }
    }

    [Fact]
    public async Task Save_WhenTheTransactionThrows_RollsBackTheSubmissionAndTheOutboxEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var memberOrgId = ObjectId.GenerateNewId().ToString();
        var correlationId = "abort-" + Guid.NewGuid().ToString("N");
        var repository = fixture.Services.GetRequiredService<IPomSubmissionRepository>();
        var outbox = fixture.Services.GetRequiredService<IOutboxRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var mapper = fixture.Services.GetRequiredService<IPomSubmissionMapper>();
        var request = SamplePomSubmission.Request() with { MemberOrgId = memberOrgId };
        var submission = mapper.ToNewSubmission(request, PomSubmissionApiFixture.CreatedAt);
        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(
            [(submission.SchemeOrgId, submission.CompliancePeriodYear, false)],
            PomSubmissionApiFixture.CreatedAt,
            correlationId,
            TestAuthentication.UserId);

        try
        {
            Task Act() => unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await repository.CreateAsync(transaction.Session, submission, transactionCancellationToken);
                    await outbox.AddAsync(transaction.Session, events, transactionCancellationToken);
                    return FailSave<PomSubmissionModel>();
                },
                cancellationToken);

            await Assert.ThrowsAsync<InvalidOperationException>(Act);
            Assert.Empty(await MemberSubmissions(memberOrgId, cancellationToken));
            Assert.Empty(await EventsFor(correlationId, cancellationToken));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    [Fact]
    public async Task AddAsync_WhenTheEventListIsEmpty_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbox = fixture.Services.GetRequiredService<IOutboxRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var before = await OutboxCount(cancellationToken);

        await unitOfWork.ExecuteAsync(
            async (transaction, transactionCancellationToken) =>
            {
                await outbox.AddAsync(transaction.Session, [], transactionCancellationToken);
                return 0;
            },
            cancellationToken);

        Assert.Equal(before, await OutboxCount(cancellationToken));
    }

    [Fact]
    public async Task AddAsync_WhenTheSessionOrEventsAreMissing_Throws()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbox = fixture.Services.GetRequiredService<IOutboxRepository>();
        var events = PomSubmissionOutboxEvents.GetOutboxEventsBySchemeYears(
            [(SamplePomSubmission.SchemeOrgId, SamplePomSubmission.CompliancePeriodYear, false)],
            PomSubmissionApiFixture.CreatedAt,
            "correlation-1",
            TestAuthentication.UserId);

        var missingSession = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AddAsync(null!, events, cancellationToken));
        var missingEvents = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            outbox.AddAsync(SubstituteSession(), null!, cancellationToken));

        Assert.Equal("session", missingSession.ParamName);
        Assert.Equal("events", missingEvents.ParamName);
    }

    [Fact]
    public async Task ListIndexes_WhenTheApplicationHasStarted_DefinesTheRelayAndRetentionIndexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var indexes = await fixture.Outbox.Indexes.List(cancellationToken).ToListAsync(cancellationToken);

        var sentByTime = Index(indexes, "sent_occurred_at");
        Assert.Equal(1, sentByTime["key"]["sent"].AsInt32);
        Assert.Equal(1, sentByTime["key"]["occurredAtUtc"].AsInt32);
        Assert.False(sentByTime.Contains("partialFilterExpression"));
        Assert.False(sentByTime.Contains("unique"));

        var expiresAt = Index(indexes, "expires_at");
        Assert.Equal(1, expiresAt["key"]["expiresAtUtc"].AsInt32);
        Assert.Equal(0, expiresAt["expireAfterSeconds"].AsInt32);

        var subjectVersion = Index(indexes, "subject_version");
        Assert.Equal(1, subjectVersion["key"]["subjectId"].AsInt32);
        Assert.Equal(1, subjectVersion["key"]["version"].AsInt32);
        Assert.True(subjectVersion["unique"].AsBoolean);
    }

    [Fact]
    public async Task AddAsync_WhenTheSubjectAndVersionAlreadyExist_RejectsTheInsert()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var schemeOrgId = ObjectId.GenerateNewId().ToString();
        var correlationId = "duplicate-version-" + Guid.NewGuid().ToString("N");
        var outbox = fixture.Services.GetRequiredService<IOutboxRepository>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        var version = OutboxEvents.CommitVersion(PomSubmissionApiFixture.CreatedAt);
        var first = UnsentEvent(schemeOrgId, 2026, version, correlationId);
        var duplicate = UnsentEvent(schemeOrgId, 2026, version, correlationId);

        try
        {
            await unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await outbox.AddAsync(transaction.Session, [first], transactionCancellationToken);
                    return 0;
                },
                cancellationToken);

            Task Act() => unitOfWork.ExecuteAsync(
                async (transaction, transactionCancellationToken) =>
                {
                    await outbox.AddAsync(transaction.Session, [duplicate], transactionCancellationToken);
                    return 0;
                },
                cancellationToken);

            var exception = await Assert.ThrowsAnyAsync<MongoException>(Act);
            Assert.Contains("E11000", exception.ToString(), StringComparison.Ordinal);
            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            Assert.Equal(first.Id, stored["_id"].AsString);
        }
        finally
        {
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    private async Task RejectOutboxInserts(CancellationToken cancellationToken)
    {
        var command = new BsonDocument
        {
            { "collMod", OutboxRepository.CollectionName },
            { "validator", new BsonDocument("sent", new BsonDocument("$type", "string")) },
            { "validationLevel", "strict" },
            { "validationAction", "error" }
        };
        await fixture.Outbox.Database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
    }

    private async Task AllowOutboxInserts(CancellationToken cancellationToken)
    {
        var command = new BsonDocument
        {
            { "collMod", OutboxRepository.CollectionName },
            { "validator", new BsonDocument() },
            { "validationLevel", "off" },
            { "validationAction", "warn" }
        };
        await fixture.Outbox.Database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken);
    }

    private Task<List<BsonDocument>> EventsFor(string correlationId, CancellationToken cancellationToken) =>
        fixture.Outbox
            .Find(CorrelationFilter(correlationId))
            .Sort(Builders<BsonDocument>.Sort.Ascending("_id"))
            .ToListAsync(cancellationToken);

    private Task<List<BsonDocument>> EventsForScheme(string schemeOrgId, CancellationToken cancellationToken) =>
        fixture.Outbox.Find(SchemeFilter(schemeOrgId)).ToListAsync(cancellationToken);

    private async Task DeleteSaved(string schemeOrgId, string correlationId, CancellationToken cancellationToken)
    {
        await DeleteScheme(schemeOrgId, cancellationToken);
        await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
    }

    private async Task DeleteScheme(string schemeOrgId, CancellationToken cancellationToken)
    {
        await fixture.Submissions.DeleteManyAsync(SchemeFilter(schemeOrgId), cancellationToken);
        await fixture.Outbox.DeleteManyAsync(SchemeFilter(schemeOrgId), cancellationToken);
    }

    private static string SubjectId(string schemeOrgId, int year) => $"{schemeOrgId}:{year}";

    private static PomSubmissionOutboxEvent UnsentEvent(
        string schemeOrgId,
        int year,
        long version,
        string correlationId) =>
        new()
        {
            Id = OutboxEvents.NewId(),
            EventType = PomSubmissionOutboxEvent.PomSubmissionCreatedType,
            SchemaVersion = PomSubmissionOutboxEvents.CurrentSchemaVersion,
            SubjectId = SubjectId(schemeOrgId, year),
            Version = version,
            PartitionKey = schemeOrgId,
            OccurredAtUtc = PomSubmissionApiFixture.CreatedAt,
            ActorId = TestAuthentication.UserId,
            CorrelationId = correlationId,
            Sent = false,
            SchemeOrgId = schemeOrgId,
            CompliancePeriodYear = year
        };

    private static void AssertUnsentEvent(
        BsonDocument stored,
        string schemeOrgId,
        int year,
        string correlationId,
        string eventType,
        DateTime? occurredAtUtc = null)
    {
        var occurredAt = occurredAtUtc ?? PomSubmissionApiFixture.CreatedAt;
        Assert.Equal(BsonType.String, stored["_id"].BsonType);
        Assert.True(Guid.TryParse(stored["_id"].AsString, out _));
        Assert.Equal(eventType, stored["eventType"].AsString);
        Assert.Equal(PomSubmissionOutboxEvents.CurrentSchemaVersion, stored["schemaVersion"].AsInt32);
        Assert.Equal(SubjectId(schemeOrgId, year), stored["subjectId"].AsString);
        Assert.Equal(OutboxEvents.CommitVersion(occurredAt), stored["version"].AsInt64);
        Assert.Equal(schemeOrgId, stored["partitionKey"].AsString);
        Assert.Equal(BsonType.ObjectId, stored["schemeOrgId"].BsonType);
        Assert.Equal(schemeOrgId, stored["schemeOrgId"].AsObjectId.ToString());
        Assert.Equal(year, stored["compliancePeriodYear"].AsInt32);
        Assert.Equal(occurredAt, stored["occurredAtUtc"].ToUniversalTime());
        Assert.Equal(TestAuthentication.UserId, stored["actorId"].AsString);
        Assert.Equal(correlationId, stored["correlationId"].AsString);
        Assert.False(stored["sent"].AsBoolean);
        Assert.False(stored.Contains("sentAtUtc"));
        Assert.False(stored.Contains("expiresAtUtc"));
        Assert.False(stored.Contains("_t"));
        Assert.False(stored.Contains("additionalFields"));
    }

    private static FilterDefinition<BsonDocument> CorrelationFilter(string correlationId) =>
        Builders<BsonDocument>.Filter.Eq("correlationId", correlationId);

    private static FilterDefinition<BsonDocument> SchemeFilter(string schemeOrgId) =>
        Builders<BsonDocument>.Filter.Eq("schemeOrgId", ObjectId.Parse(schemeOrgId));

    private static IClientSessionHandle SubstituteSession() =>
        Substitute.For<IClientSessionHandle>();

    private static T FailSave<T>() =>
        throw new InvalidOperationException("fail the save");

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex CorrelationIdPattern();
}
