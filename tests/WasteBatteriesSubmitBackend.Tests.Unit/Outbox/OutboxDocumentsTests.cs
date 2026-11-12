using MongoDB.Bson;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.PomSubmission.Outbox;
using WasteBatteriesSubmitBackend.Tests.Common;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Outbox;

public class OutboxDocumentsTests
{
    [Fact]
    public void ToBsonDocument_WhenTheEventIsASubmission_StoresTheSubclassFieldsBesideTheSharedOnes()
    {
        MongoConventions.Register();
        var occurredAt = SamplePomSubmission.CreatedAt;
        var outboxEvent = new PomSubmissionOutboxEvent
        {
            Id = "3f1a6c0e-4b2d-4e7a-9c11-8d5e2a7b6c40",
            EventType = PomSubmissionOutboxEvent.PomSubmissionCreatedType,
            SchemaVersion = OutboxEvent.CurrentSchemaVersion,
            SubjectId = $"{SamplePomSubmission.SchemeOrgId}:{SamplePomSubmission.CompliancePeriodYear}",
            Version = OutboxEvents.CommitVersion(occurredAt),
            PartitionKey = SamplePomSubmission.SchemeOrgId,
            OccurredAtUtc = occurredAt,
            ActorId = "actor-1",
            CorrelationId = "correlation-1",
            Sent = false,
            SchemeOrgId = SamplePomSubmission.SchemeOrgId,
            CompliancePeriodYear = SamplePomSubmission.CompliancePeriodYear,
            AdditionalFields = new BsonDocument("batchRef", "upload-1")
        };

        var document = OutboxDocuments.ToBsonDocument(outboxEvent);

        Assert.False(document.Contains("_t"));
        Assert.False(document.Contains("additionalFields"));
        Assert.Equal(outboxEvent.Id, document["_id"].AsString);
        Assert.Equal(PomSubmissionOutboxEvent.PomSubmissionCreatedType, document["eventType"].AsString);
        Assert.Equal(BsonType.ObjectId, document["schemeOrgId"].BsonType);
        Assert.Equal(SamplePomSubmission.SchemeOrgId, document["schemeOrgId"].AsObjectId.ToString());
        Assert.Equal(SamplePomSubmission.CompliancePeriodYear, document["compliancePeriodYear"].AsInt32);
        Assert.Equal("upload-1", document["batchRef"].AsString);
        Assert.False(document.Contains("sentAtUtc"));
        Assert.False(document.Contains("expiresAtUtc"));
    }
}
