using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WasteBatteriesSubmitBackend.Outbox;

// The fields every outbox event has. A feature adds its own properties on a subclass.
// AdditionalFields is for values that do not have a property yet. They are stored as their own
// elements, beside the properties, not nested under this name.
// Sent, SentAtUtc and ExpiresAtUtc stay unset until a relay publishes the event.
// That relay is not part of this service. An unsent event has no ExpiresAtUtc, so the TTL index leaves it.
[BsonIgnoreExtraElements]
public abstract class OutboxEvent
{
    // The shape of the shared fields. A new shared field or meaning is a code change, so this is not configuration.
    public const int CurrentSchemaVersion = 1;

    // A GUID. The id is stable across a retry; it is not the ordering token.
    [BsonId]
    public required string Id { get; init; }

    public required string EventType { get; init; }

    public int SchemaVersion { get; init; }

    // What changed. The feature that builds the event chooses the text.
    public required string SubjectId { get; init; }

    // Commit time in unix milliseconds. A GUID does not order a later message against an earlier one.
    // Unique together with SubjectId. Different subjects written in one save share this value.
    public long Version { get; init; }

    // Who to act for. No personal details.
    public required string PartitionKey { get; init; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime OccurredAtUtc { get; init; }

    // The caller's opaque id. No name or other personal details.
    public required string ActorId { get; init; }

    public required string CorrelationId { get; init; }

    public bool Sent { get; init; }

    // Omitted until the relay publishes. A null would still be stored, and the template leaves it empty.
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SentAtUtc { get; init; }

    // Omitted while unsent. The TTL index only deletes a document once this date is set.
    [BsonIgnoreIfNull]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ExpiresAtUtc { get; init; }

    [BsonExtraElements]
    public BsonDocument AdditionalFields { get; init; } = new();
}
