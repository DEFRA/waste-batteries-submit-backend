using System.Text.Json.Nodes;
using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Outbox.Relay;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox.Relay;

public class OutboxMessageTests
{
    [Fact]
    public void ToJson_WhenTheDocumentIsASubmission_PublishesTheEnvelopeAndFeatureFields()
    {
        MongoConventions.Register();
        var occurredAt = SamplePomSubmission.CreatedAt;
        var outboxEvent = new PomSubmissionOutboxEvent
        {
            Id = "3f1a6c0e-4b2d-4e7a-9c11-8d5e2a7b6c40",
            EventType = PomSubmissionOutboxEvent.PomSubmissionCreatedType,
            SchemaVersion = PomSubmissionOutboxEvents.CurrentSchemaVersion,
            SubjectId = $"{SamplePomSubmission.SchemeOrgId}:{SamplePomSubmission.CompliancePeriodYear}",
            Version = OutboxEvents.CommitVersion(occurredAt),
            PartitionKey = SamplePomSubmission.SchemeOrgId,
            OccurredAtUtc = occurredAt,
            ActorId = "actor-1",
            CorrelationId = "correlation-1",
            Sent = false,
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            CompliancePeriodYear = SamplePomSubmission.CompliancePeriodYear
        };
        var document = OutboxDocuments.ToBsonDocument(outboxEvent);
        document["sentAtUtc"] = occurredAt;
        document["expiresAtUtc"] = occurredAt.AddDays(90);
        document["claimedBy"] = "relay-1";
        document["claimedUntilUtc"] = occurredAt.AddMinutes(1);
        document["attemptCount"] = 2;
        document["nextAttemptAtUtc"] = occurredAt.AddSeconds(4);
        document["flag"] = true;
        document["ratio"] = 1.5;
        document["weight"] = Decimal128.Parse("1.25");
        document["notedAt"] = occurredAt;
        document["empty"] = BsonNull.Value;
        document["child"] = new BsonDocument("name", "cell");
        document["tags"] = new BsonArray { "portable", 2 };

        var json = OutboxMessage.ToJson(document);

        Assert.StartsWith("{\"eventId\":", json, StringComparison.Ordinal);
        var envelope = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(outboxEvent.Id, envelope["eventId"]!.GetValue<string>());
        Assert.Equal(PomSubmissionOutboxEvent.PomSubmissionCreatedType, envelope["eventType"]!.GetValue<string>());
        Assert.Equal(PomSubmissionOutboxEvents.CurrentSchemaVersion, envelope["schemaVersion"]!.GetValue<int>());
        Assert.Equal(outboxEvent.SubjectId, envelope["subjectId"]!.GetValue<string>());
        Assert.Equal(outboxEvent.Version, envelope["version"]!.GetValue<long>());
        Assert.Equal(outboxEvent.PartitionKey, envelope["partitionKey"]!.GetValue<string>());
        Assert.Equal("2026-10-21T09:00:00.000Z", envelope["occurredAt"]!.GetValue<string>());
        Assert.Equal("actor-1", envelope["actorId"]!.GetValue<string>());
        Assert.Equal("correlation-1", envelope["correlationId"]!.GetValue<string>());
        Assert.Equal(SamplePomSubmission.SchemeOrgId, envelope["schemeOrgId"]!.GetValue<string>());
        Assert.Equal(SamplePomSubmission.CompliancePeriodYear, envelope["compliancePeriodYear"]!.GetValue<int>());
        Assert.True(envelope["flag"]!.GetValue<bool>());
        Assert.Equal(1.5, envelope["ratio"]!.GetValue<double>());
        Assert.Equal("1.25", envelope["weight"]!.GetValue<string>());
        Assert.Equal("2026-10-21T09:00:00.000Z", envelope["notedAt"]!.GetValue<string>());
        Assert.Null(envelope["empty"]);
        Assert.Equal("cell", envelope["child"]!["name"]!.GetValue<string>());
        Assert.Equal("portable", envelope["tags"]![0]!.GetValue<string>());
        Assert.Equal(2, envelope["tags"]![1]!.GetValue<int>());
        Assert.False(envelope.ContainsKey("_id"));
        Assert.False(envelope.ContainsKey("occurredAtUtc"));
        Assert.False(envelope.ContainsKey("sent"));
        Assert.False(envelope.ContainsKey("sentAtUtc"));
        Assert.False(envelope.ContainsKey("expiresAtUtc"));
        Assert.False(envelope.ContainsKey("claimedBy"));
        Assert.False(envelope.ContainsKey("claimedUntilUtc"));
        Assert.False(envelope.ContainsKey("attemptCount"));
        Assert.False(envelope.ContainsKey("nextAttemptAtUtc"));
    }

    [Fact]
    public void ToJson_WhenAFieldCannotBePublished_Throws()
    {
        var document = Document();
        document["blob"] = new BsonBinaryData([1, 2, 3]);

        var exception = Assert.Throws<InvalidOperationException>(() => OutboxMessage.ToJson(document));

        Assert.Contains("Binary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToJson_WhenTheEventIdIsMissing_Throws()
    {
        var document = Document();
        document.Remove("_id");

        var exception = Assert.Throws<InvalidOperationException>(() => OutboxMessage.ToJson(document));

        Assert.Contains("_id", exception.Message, StringComparison.Ordinal);
    }

    private static BsonDocument Document() => new()
    {
        { "_id", "3f1a6c0e-4b2d-4e7a-9c11-8d5e2a7b6c40" },
        { "eventType", PomSubmissionOutboxEvent.PomSubmissionCreatedType },
        { "schemaVersion", 1 },
        { "subjectId", $"{SamplePomSubmission.SchemeOrgId}:2026" },
        { "version", OutboxEvents.CommitVersion(SamplePomSubmission.CreatedAt) },
        { "partitionKey", SamplePomSubmission.SchemeOrgId },
        { "occurredAtUtc", SamplePomSubmission.CreatedAt },
        { "actorId", "actor-1" },
        { "correlationId", "correlation-1" }
    };
}
