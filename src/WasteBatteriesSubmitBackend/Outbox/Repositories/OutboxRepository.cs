using MongoDB.Bson;
using MongoDB.Driver;
using WasteBatteriesSubmitBackend.Outbox;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Outbox.Repositories;

public interface IOutboxRepository
{
    // Inserts the events on the caller's session so they commit or roll back with the submissions.
    // An empty list writes nothing. Sent stays false and expiresAt is omitted. The relay sets those later.
    Task AddAsync(
        IClientSessionHandle session,
        IReadOnlyList<OutboxEvent> events,
        CancellationToken cancellationToken = default);

    // Takes the oldest unsent document that is due and not already claimed. Null when nothing is due.
    // The claim is conditional, so a second caller does not receive the same document.
    Task<BsonDocument?> ClaimNextAsync(
        DateTime utcNow,
        DateTime claimedUntilUtc,
        string claimedBy,
        CancellationToken cancellationToken = default);

    // Sets sent, sentAtUtc and expiresAtUtc when this caller still holds the claim.
    // Returns false when the claim has already moved on.
    Task<bool> MarkSentAsync(
        string eventId,
        string claimedBy,
        DateTime sentAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default);

    // Clears the claim and sets the next attempt when this caller still holds the claim.
    // Sent stays false. Returns false when the claim has already moved on.
    Task<bool> RecordFailureAsync(
        string eventId,
        string claimedBy,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken = default);
}

public class OutboxRepository(IMongoDbClientFactory connectionFactory, ILoggerFactory loggerFactory)
    : MongoService<OutboxEvent>(connectionFactory, CollectionName, loggerFactory), IOutboxRepository
{
    public const string CollectionName = "outbox";

    public Task AddAsync(
        IClientSessionHandle session,
        IReadOnlyList<OutboxEvent> events,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return Task.CompletedTask;
        }

        // The runtime type supplies the feature's own fields. Inserting as the base type would drop them.
        var documents = events.Select(OutboxDocuments.ToBsonDocument).ToList();
        return Documents.InsertManyAsync(session, documents, cancellationToken: cancellationToken);
    }

    public async Task<BsonDocument?> ClaimNextAsync(
        DateTime utcNow,
        DateTime claimedUntilUtc,
        string claimedBy,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(utcNow, nameof(utcNow));
        RequireUtc(claimedUntilUtc, nameof(claimedUntilUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);

        var update = Builders<BsonDocument>.Update
            .Set("claimedBy", claimedBy)
            .Set("claimedUntilUtc", claimedUntilUtc);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort.Ascending("occurredAtUtc").Ascending("_id"),
            ReturnDocument = ReturnDocument.After
        };

        // The driver declares a document, and returns null when no document is due.
        return await Documents.FindOneAndUpdateAsync(DueUnsent(utcNow), update, options, cancellationToken);
    }

    public async Task<bool> MarkSentAsync(
        string eventId,
        string claimedBy,
        DateTime sentAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);
        RequireUtc(sentAtUtc, nameof(sentAtUtc));
        RequireUtc(expiresAtUtc, nameof(expiresAtUtc));

        var update = Builders<BsonDocument>.Update
            .Set("sent", true)
            .Set("sentAtUtc", sentAtUtc)
            .Set("expiresAtUtc", expiresAtUtc)
            .Unset("claimedBy")
            .Unset("claimedUntilUtc")
            .Unset("attemptCount")
            .Unset("nextAttemptAtUtc");
        var result = await Documents.UpdateOneAsync(
            HeldClaim(eventId, claimedBy),
            update,
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    public async Task<bool> RecordFailureAsync(
        string eventId,
        string claimedBy,
        int attemptCount,
        DateTime nextAttemptAtUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);
        RequireUtc(nextAttemptAtUtc, nameof(nextAttemptAtUtc));
        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }

        var update = Builders<BsonDocument>.Update
            .Set("attemptCount", attemptCount)
            .Set("nextAttemptAtUtc", nextAttemptAtUtc)
            .Unset("claimedBy")
            .Unset("claimedUntilUtc");
        var result = await Documents.UpdateOneAsync(
            HeldClaim(eventId, claimedBy),
            update,
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    protected override List<CreateIndexModel<OutboxEvent>> DefineIndexes(IndexKeysDefinitionBuilder<OutboxEvent> builder) =>
        OutboxIndexes.Create(builder);

    private IMongoCollection<BsonDocument> Documents =>
        Collection.Database.GetCollection<BsonDocument>(CollectionName);

    // sent is false, the claim is missing or already over, and the next attempt is missing or due.
    // occurredAtUtc then _id is the order a pass publishes in.
    private static FilterDefinition<BsonDocument> DueUnsent(DateTime utcNow)
    {
        var filter = Builders<BsonDocument>.Filter;
        return filter.And(
            filter.Eq("sent", false),
            filter.Or(filter.Exists("claimedUntilUtc", false), filter.Lte("claimedUntilUtc", utcNow)),
            filter.Or(filter.Exists("nextAttemptAtUtc", false), filter.Lte("nextAttemptAtUtc", utcNow)));
    }

    private static FilterDefinition<BsonDocument> HeldClaim(string eventId, string claimedBy)
    {
        var filter = Builders<BsonDocument>.Filter;
        return filter.And(
            filter.Eq("_id", eventId),
            filter.Eq("claimedBy", claimedBy),
            filter.Eq("sent", false));
    }

    private static void RequireUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be UTC.", paramName);
        }
    }
}
