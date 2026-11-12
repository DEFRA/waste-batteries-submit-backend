using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using WasteBatteriesSubmitBackend.Outbox;

namespace WasteBatteriesSubmitBackend.PomSubmission.Outbox;

// An event per schemeid and compliance year were saved.
[BsonIgnoreExtraElements]
public sealed class PomSubmissionOutboxEvent : OutboxEvent
{
    public const string PomSubmissionCreatedType = "PomSubmissionCreated";

    public const string PomSubmissionAmendedType = "PomSubmissionAmended";

    // A scheme-year that both creates and amends in one save. The consumer re-reads the current rows.
    public const string PomSubmissionSavedType = "PomSubmissionSaved";

    [BsonRepresentation(BsonType.ObjectId)]
    public required string SchemeOrgId { get; init; }

    public int CompliancePeriodYear { get; init; }
}
