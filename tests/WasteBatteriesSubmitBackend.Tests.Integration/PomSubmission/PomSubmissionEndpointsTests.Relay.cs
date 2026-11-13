using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.Outbox.Repositories;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Time;

namespace WasteBatteriesSubmitBackend.Tests.Integration.PomSubmission;

public partial class PomSubmissionEndpointsTests
{
    [Fact]
    public async Task ClaimNext_WhenTwoDocumentsAreUnsent_ReturnsTheEarlierOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlierAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var laterAt = earlierAt.AddMinutes(5);
        var earlier = UnsentDocument("00000000-0000-0000-0000-000000000011", earlierAt, 2031);
        var later = UnsentDocument("00000000-0000-0000-0000-000000000012", laterAt, 2032);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertManyAsync([later, earlier], cancellationToken: cancellationToken);

            var claimed = await repository.ClaimNextAsync(laterAt.AddHours(1), laterAt.AddHours(2), "relay-a", cancellationToken);

            Assert.NotNull(claimed);
            Assert.Equal(earlier["_id"].AsString, claimed["_id"].AsString);
            Assert.Equal("relay-a", claimed["claimedBy"].AsString);
        }
        finally
        {
            await DeleteOutbox(earlier["_id"].AsString, later["_id"].AsString, cancellationToken);
        }
    }

    [Fact]
    public async Task ClaimNext_WhenADocumentIsSentOrNotYetDueOrStillClaimed_SkipsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var sent = UnsentDocument("00000000-0000-0000-0000-000000000021", now.AddMinutes(-3), 2033);
        sent["sent"] = true;
        var leased = UnsentDocument("00000000-0000-0000-0000-000000000022", now.AddMinutes(-2), 2034);
        leased["claimedBy"] = "other";
        leased["claimedUntilUtc"] = now.AddMinutes(10);
        var waiting = UnsentDocument("00000000-0000-0000-0000-000000000023", now.AddMinutes(-1), 2035);
        waiting["nextAttemptAtUtc"] = now.AddMinutes(10);
        waiting["attemptCount"] = 1;
        var due = UnsentDocument("00000000-0000-0000-0000-000000000024", now, 2036);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertManyAsync([sent, leased, waiting, due], cancellationToken: cancellationToken);

            var claimed = await repository.ClaimNextAsync(now, now.AddMinutes(1), "relay-a", cancellationToken);

            Assert.NotNull(claimed);
            Assert.Equal(due["_id"].AsString, claimed["_id"].AsString);
            var stillWaiting = await fixture.Outbox.Find(Builders<BsonDocument>.Filter.Eq("_id", waiting["_id"])).SingleAsync(cancellationToken);
            Assert.False(stillWaiting.Contains("claimedBy"));
        }
        finally
        {
            await DeleteOutbox(
                sent["_id"].AsString,
                leased["_id"].AsString,
                waiting["_id"].AsString,
                due["_id"].AsString,
                cancellationToken);
        }
    }

    [Fact]
    public async Task ClaimNext_WhenTwoCallersClaimOneDocument_OnlyOneWins()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        var document = UnsentDocument("00000000-0000-0000-0000-000000000031", now, 2037);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertOneAsync(document, cancellationToken: cancellationToken);
            var until = now.AddMinutes(1);

            var results = await Task.WhenAll(
                repository.ClaimNextAsync(now, until, "relay-a", cancellationToken),
                repository.ClaimNextAsync(now, until, "relay-b", cancellationToken));

            var winner = Assert.Single(results, claimed => claimed is not null);
            Assert.Equal(document["_id"].AsString, winner!["_id"].AsString);
        }
        finally
        {
            await DeleteOutbox(document["_id"].AsString, cancellationToken);
        }
    }

    [Fact]
    public async Task ClaimNext_WhenTheLeaseHasExpired_AnotherCallerCanClaimIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var document = UnsentDocument("00000000-0000-0000-0000-000000000041", now, 2038);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertOneAsync(document, cancellationToken: cancellationToken);
            var held = await repository.ClaimNextAsync(now, now.AddSeconds(60), "relay-a", cancellationToken);
            Assert.NotNull(held);

            var blocked = await repository.ClaimNextAsync(now.AddSeconds(59), now.AddMinutes(2), "relay-b", cancellationToken);
            var reclaimed = await repository.ClaimNextAsync(now.AddSeconds(60), now.AddMinutes(2), "relay-b", cancellationToken);

            Assert.Null(blocked);
            Assert.NotNull(reclaimed);
            Assert.Equal("relay-b", reclaimed["claimedBy"].AsString);
            Assert.Equal(document["_id"].AsString, reclaimed["_id"].AsString);
        }
        finally
        {
            await DeleteOutbox(document["_id"].AsString, cancellationToken);
        }
    }

    [Fact]
    public async Task MarkSent_WhenTheCallerStillHoldsTheClaim_SetsSentAndTheRetentionTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var document = UnsentDocument("00000000-0000-0000-0000-000000000051", now, 2039);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertOneAsync(document, cancellationToken: cancellationToken);
            await repository.ClaimNextAsync(now, now.AddMinutes(1), "relay-a", cancellationToken);
            var sentAt = now.AddSeconds(2);

            var lost = await repository.MarkSentAsync(document["_id"].AsString, "relay-b", sentAt, sentAt.AddDays(90), cancellationToken);
            var marked = await repository.MarkSentAsync(document["_id"].AsString, "relay-a", sentAt, sentAt.AddDays(90), cancellationToken);
            var stored = await fixture.Outbox.Find(Builders<BsonDocument>.Filter.Eq("_id", document["_id"])).SingleAsync(cancellationToken);

            Assert.False(lost);
            Assert.True(marked);
            Assert.True(stored["sent"].AsBoolean);
            Assert.Equal(sentAt, stored["sentAtUtc"].ToUniversalTime());
            Assert.Equal(sentAt.AddDays(90), stored["expiresAtUtc"].ToUniversalTime());
            Assert.False(stored.Contains("claimedBy"));
            Assert.False(stored.Contains("claimedUntilUtc"));
            Assert.Equal(document["_id"].AsString, stored["_id"].AsString);
        }
        finally
        {
            await DeleteOutbox(document["_id"].AsString, cancellationToken);
        }
    }

    [Fact]
    public async Task RecordFailure_WhenTheCallerStillHoldsTheClaim_ClearsTheClaimAndKeepsItUnsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var now = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);
        var document = UnsentDocument("00000000-0000-0000-0000-000000000061", now, 2040);
        var repository = Repository();

        try
        {
            await fixture.Outbox.InsertOneAsync(document, cancellationToken: cancellationToken);
            await repository.ClaimNextAsync(now, now.AddMinutes(1), "relay-a", cancellationToken);
            var nextAttempt = now.AddSeconds(4);

            var lost = await repository.RecordFailureAsync(document["_id"].AsString, "relay-b", 1, nextAttempt, cancellationToken);
            var recorded = await repository.RecordFailureAsync(document["_id"].AsString, "relay-a", 1, nextAttempt, cancellationToken);
            var stored = await fixture.Outbox.Find(Builders<BsonDocument>.Filter.Eq("_id", document["_id"])).SingleAsync(cancellationToken);

            Assert.False(lost);
            Assert.True(recorded);
            Assert.False(stored["sent"].AsBoolean);
            Assert.False(stored.Contains("claimedBy"));
            Assert.Equal(1, stored["attemptCount"].AsInt32);
            Assert.Equal(nextAttempt, stored["nextAttemptAtUtc"].ToUniversalTime());
            Assert.Equal(document["_id"].AsString, stored["_id"].AsString);
        }
        finally
        {
            await DeleteOutbox(document["_id"].AsString, cancellationToken);
        }
    }

    [Fact]
    public async Task PublishPending_WhenASubmissionIsCreated_MarksTheSameEventSent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f67890124b1";
        const string correlationId = "relay-sent";
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);
        var publisher = new RecordingPublisher();
        var relay = Relay(publisher);

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
            await relay.PublishPendingAsync(cancellationToken);

            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            var published = Assert.Single(publisher.Published);
            var envelope = JsonNode.Parse(OutboxMessage.ToJson(published))!;
            Assert.True(stored["sent"].AsBoolean);
            Assert.Equal(stored["_id"].AsString, published["_id"].AsString);
            Assert.Equal(stored["_id"].AsString, envelope["eventId"]!.GetValue<string>());
            Assert.Equal(stored["version"].AsInt64, envelope["version"]!.GetValue<long>());
            Assert.Equal(stored["sentAtUtc"].ToUniversalTime().AddDays(90), stored["expiresAtUtc"].ToUniversalTime());
            Assert.False(stored.Contains("claimedBy"));
            Assert.False(envelope.AsObject().ContainsKey("sent"));
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    [Fact]
    public async Task PublishPending_WhenThePublisherThrows_LeavesTheSubmissionEventUnsent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string memberOrgId = "674a1b2c3d4e5f67890124b2";
        const string correlationId = "relay-failed";
        using var client = fixture.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-cdp-request-id", correlationId);
        var publisher = new RecordingPublisher { Exception = new InvalidOperationException("sns down") };
        var relay = Relay(publisher);

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
            var before = Assert.Single(await EventsFor(correlationId, cancellationToken));
            await relay.PublishPendingAsync(cancellationToken);
            publisher.Exception = null;
            await relay.PublishPendingAsync(cancellationToken);

            var stored = Assert.Single(await EventsFor(correlationId, cancellationToken));
            Assert.False(stored["sent"].AsBoolean);
            Assert.Equal(before["_id"].AsString, stored["_id"].AsString);
            Assert.False(stored.Contains("claimedBy"));
            Assert.Equal(1, stored["attemptCount"].AsInt32);
            Assert.True(stored.Contains("nextAttemptAtUtc"));
            Assert.Empty(publisher.Published);
        }
        finally
        {
            await fixture.Submissions.DeleteManyAsync(MemberFilter(memberOrgId), cancellationToken);
            await fixture.Outbox.DeleteManyAsync(CorrelationFilter(correlationId), cancellationToken);
        }
    }

    private IOutboxRepository Repository() => fixture.Services.GetRequiredService<IOutboxRepository>();

    private OutboxRelay Relay(RecordingPublisher publisher) =>
        new(
            Repository,
            publisher,
            Options.Create(new OutboxRelayOptions
            {
                Enabled = true,
                TopicArn = "arn:aws:sns:eu-west-2:000000000000:waste_batteries_submit_events",
                BatchSize = 20,
                LeaseSeconds = 60,
                RetentionDays = 90,
                PollIntervalSeconds = 5,
                RetryInitialDelaySeconds = 1,
                RetryMaxDelaySeconds = 300,
                RetryMultiplier = 2
            }),
            fixture.Services.GetRequiredService<ITimeProvider>(),
            NullLogger<OutboxRelay>.Instance);

    private Task DeleteOutbox(CancellationToken cancellationToken, params string[] ids) =>
        fixture.Outbox.DeleteManyAsync(
            Builders<BsonDocument>.Filter.In("_id", ids),
            cancellationToken);

    private Task DeleteOutbox(string first, string second, CancellationToken cancellationToken) =>
        DeleteOutbox(cancellationToken, first, second);

    private Task DeleteOutbox(string first, string second, string third, string fourth, CancellationToken cancellationToken) =>
        DeleteOutbox(cancellationToken, first, second, third, fourth);

    private Task DeleteOutbox(string id, CancellationToken cancellationToken) =>
        DeleteOutbox(cancellationToken, id);

    private static BsonDocument UnsentDocument(string id, DateTime occurredAtUtc, int year)
    {
        var schemeOrgId = PomSubmissionApiFixture.SchemeOrgId;
        return new BsonDocument
        {
            { "_id", id },
            { "eventType", PomSubmissionOutboxEvent.PomSubmissionCreatedType },
            { "schemaVersion", PomSubmissionOutboxEvents.CurrentSchemaVersion },
            { "subjectId", $"{schemeOrgId}:{year}" },
            { "version", OutboxEvents.CommitVersion(occurredAtUtc) },
            { "partitionKey", schemeOrgId },
            { "occurredAtUtc", occurredAtUtc },
            { "actorId", "actor-1" },
            { "correlationId", id },
            { "sent", false },
            { "schemeOrgId", ObjectId.Parse(schemeOrgId) },
            { "compliancePeriodYear", year }
        };
    }

    private sealed class RecordingPublisher : IOutboxPublisher
    {
        public List<BsonDocument> Published { get; } = [];

        public Exception? Exception { get; set; }

        public Task PublishAsync(BsonDocument document, CancellationToken cancellationToken = default)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            Published.Add(document.DeepClone().AsBsonDocument);
            return Task.CompletedTask;
        }
    }
}
