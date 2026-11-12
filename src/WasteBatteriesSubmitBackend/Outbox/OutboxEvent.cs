using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Diagnostics.CodeAnalysis;

namespace WasteBatteriesSubmitBackend.Outbox;

// One unpublished fact that submissions for a scheme and compliance year were saved.
// The id is chosen before the transaction, so a retry writes the same event.
// Sent, SentAtUtc and ExpiresAtUtc stay unset until a relay publishes the event.
// That relay is not part of this service. An unsent event has no ExpiresAtUtc, so the TTL index leaves it.
[BsonIgnoreExtraElements]
[method: BsonConstructor]
[method: SetsRequiredMembers]
public sealed class OutboxEvent()
{
    // The shape of this document. A new field or meaning is a code change, so this is not configuration.
    public const int CurrentSchemaVersion = 1;

    public const string PomSubmissionCreatedType = "PomSubmissionCreated";

    public const string PomSubmissionAmendedType = "PomSubmissionAmended";

    // A scheme-year that both creates and amends in one save (e.g. batch upsert that contains both).
    public const string PomSubmissionSavedType = "PomSubmissionSaved";

    // A GUID. The id is stable across a retry; it is not the ordering token.
    [BsonId]
    public required string Id { get; init; } = null!;

    public required string EventType { get; init; } = null!;

    public int SchemaVersion { get; init; }

    // The scheme and compliance year that changed, not one submission id.
    public required string SubjectId { get; init; } = null!;

    // Commit time in unix milliseconds. A scheme-year has no single submission version,
    // and a GUID does not order a later message against an earlier one.
    public long Version { get; init; }

    // The organisation to act for. The scheme id, with no personal details.
    public required string PartitionKey { get; init; } = null!;

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime OccurredAtUtc { get; init; }

    // The caller's opaque id. No name or other personal details.
    public required string ActorId { get; init; } = null!;

    public required string CorrelationId { get; init; } = null!;

    public bool Sent { get; init; }

    // Omitted until the relay publishes. A null would still be stored, and the template leaves it empty.
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SentAtUtc { get; init; }

    // Omitted while unsent. The TTL index only deletes a document once this date is set.
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ExpiresAtUtc { get; init; }

    [BsonRepresentation(BsonType.ObjectId)]
    public required string SchemeOrgId { get; init; } = null!;

    public int CompliancePeriodYear { get; init; }
}
