using MongoDB.Driver;

namespace WasteBatteriesSubmitBackend.Outbox.Repositories;

internal static class OutboxIndexes
{
    // sent then occurredAt is the relay scan: unsent events, in the order they happened.
    // expiresAt is a TTL index with expireAfterSeconds 0, so a document is removed at that instant.
    // Unsent documents omit expiresAt and are not removed.
    public static List<CreateIndexModel<OutboxEvent>> Create(IndexKeysDefinitionBuilder<OutboxEvent> builder)
    {
        var sentByTime = new CreateIndexModel<OutboxEvent>(
            builder
                .Ascending(outboxEvent => outboxEvent.Sent)
                .Ascending(outboxEvent => outboxEvent.OccurredAtUtc),
            new CreateIndexOptions { Name = "sent_occurred_at" });

        var expiresAt = new CreateIndexModel<OutboxEvent>(
            builder.Ascending(outboxEvent => outboxEvent.ExpiresAtUtc),
            new CreateIndexOptions { Name = "expires_at", ExpireAfter = TimeSpan.Zero });

        // One subject cannot store the same version twice, or a consumer cannot order those events.
        // Version alone is not unique: one save writes several subjects at one commit time.
        var subjectVersion = new CreateIndexModel<OutboxEvent>(
            builder
                .Ascending(outboxEvent => outboxEvent.SubjectId)
                .Ascending(outboxEvent => outboxEvent.Version),
            new CreateIndexOptions { Name = "subject_version", Unique = true });

        return [sentByTime, expiresAt, subjectVersion];
    }
}
