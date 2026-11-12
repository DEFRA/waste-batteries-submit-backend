using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
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
        const string memberOrgId = "674a1b2c3d4e5f67890124a1";
        const string correlationId = "create-outbox";
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);

        try
        {
            var response = await client.PostAsync(
                "/pom-submissions",
                Json(SubmissionBody(
                    PomSubmissionApiFixture.SchemeOrgId,
                    memberOrgId,
                    SamplePomSubmission.CompliancePeriodYear,
                    SamplePomSubmission.CompliancePeriodQuarter)),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            Snapshot.Match(OutboxJson(stored));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    [Fact]
    public async Task Create_WhenTheTraceHeaderIsAbsent_StoresACorrelationId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f67890124a2";
        using var client = fixture.CreateClient();

        try
        {
            var response = await client.PostAsync(
                "/pom-submissions",
                Json(SubmissionBody(
                    PomSubmissionApiFixture.SchemeOrgId,
                    memberOrgId,
                    SamplePomSubmission.CompliancePeriodYear,
                    SamplePomSubmission.CompliancePeriodQuarter)),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            // The stored fields match the create snapshot. This request has no trace header,
            // so the only new value is the generated correlation id.
            var stored = Assert.Single(await EventsForScheme(PomSubmissionApiFixture.SchemeOrgId, cancellationToken));
            Assert.Matches(CorrelationIdPattern(), stored["correlationId"].AsString);
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(SchemeFilter(PomSubmissionApiFixture.SchemeOrgId), cancellationToken);
        }
    }

    [Fact]
    public async Task Amend_WhenTheSubmissionIsAmended_WritesOneUnsentOutboxEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f67890124a3";
        const string correlationId = "amend-outbox";
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
            Snapshot.Match(OutboxJson(stored));
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
        const string schemeA = "674a1b2c3d4e5f6789012411";
        const string schemeB = "674a1b2c3d4e5f6789012412";
        const string member1 = "674a1b2c3d4e5f6789012413";
        const string member2 = "674a1b2c3d4e5f6789012414";
        const string correlationId = "batch-outbox";
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
            Snapshot.Match(OutboxJson(events));
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

    // The event id is a new GUID on every save. Pin it so the file stays stable.
    // One document is written as an object. Several are written as an array, in subject order.
    private static string OutboxJson(BsonDocument stored) => OutboxJson([stored]);

    private static string OutboxJson(IReadOnlyList<BsonDocument> stored)
    {
        var documents = stored
            .Select(document => document.DeepClone().AsBsonDocument)
            .OrderBy(document => document["subjectId"].AsString, StringComparer.Ordinal)
            .ToList();
        var ids = documents.Select(document => document["_id"].AsString).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        for (var index = 0; index < documents.Count; index++)
        {
            Assert.True(Guid.TryParse(ids[index], out _));
            documents[index]["_id"] = $"00000000-0000-0000-0000-{(index + 1):D12}";
        }

        BsonValue value = documents.Count == 1 ? documents[0] : new BsonArray(documents);
        return value.ToJson(CanonicalJson);
    }

    private static readonly JsonWriterSettings CanonicalJson = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson,
        Indent = true,
        NewLineChars = "\n"
    };

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
