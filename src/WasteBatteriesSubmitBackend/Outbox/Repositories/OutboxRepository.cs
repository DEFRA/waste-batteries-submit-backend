using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Linq.Expressions;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Outbox.Repositories;

public interface IOutboxRepository
{
    // Inserts the events on the caller's session so they commit or roll back with the database inserts/updates.
    // An empty list writes nothing. Sent stays false and expiresAt is omitted. The relay sets those later.
    Task AddAsync(
        IClientSessionHandle session,
        IReadOnlyList<OutboxEvent> events,
        CancellationToken cancellationToken = default);

    // Takes the oldest unsent document of these event types that is due and not already claimed.
    // Null when nothing is due. The claim is conditional, so a second caller does not receive the same document.
    Task<BsonDocument?> ClaimNextAsync(
        DateTime utcNow,
        DateTime claimedUntilUtc,
        string claimedBy,
        IReadOnlyCollection<string> eventTypes,
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
        IReadOnlyCollection<string> eventTypes,
        CancellationToken cancellationToken = default)
    {
        RequireUtc(utcNow, nameof(utcNow));
        RequireUtc(claimedUntilUtc, nameof(claimedUntilUtc));
        ArgumentException.ThrowIfNullOrWhiteSpace(claimedBy);
        ArgumentNullException.ThrowIfNull(eventTypes);
        if (eventTypes.Count == 0 || eventTypes.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one event type is required.", nameof(eventTypes));
        }

        var update = Builders<BsonDocument>.Update
            .Set(Field(outboxEvent => outboxEvent.ClaimedBy), claimedBy)
            .Set(Field(outboxEvent => outboxEvent.ClaimedUntilUtc), claimedUntilUtc);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort
                .Ascending(Field(outboxEvent => outboxEvent.OccurredAtUtc))
                .Ascending(Field(outboxEvent => outboxEvent.Id)),
            ReturnDocument = ReturnDocument.After
        };

        // The driver declares a document, and returns null when no document of these types is due.
        return await Documents.FindOneAndUpdateAsync(DueUnsent(utcNow, eventTypes), update, options, cancellationToken);
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
            .Set(Field(outboxEvent => outboxEvent.Sent), true)
            .Set(Field(outboxEvent => outboxEvent.SentAtUtc), sentAtUtc)
            .Set(Field(outboxEvent => outboxEvent.ExpiresAtUtc), expiresAtUtc)
            .Unset(Field(outboxEvent => outboxEvent.ClaimedBy))
            .Unset(Field(outboxEvent => outboxEvent.ClaimedUntilUtc))
            .Unset(Field(outboxEvent => outboxEvent.AttemptCount))
            .Unset(Field(outboxEvent => outboxEvent.NextAttemptAtUtc));
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
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 1);

        var update = Builders<BsonDocument>.Update
            .Set(Field(outboxEvent => outboxEvent.AttemptCount), attemptCount)
            .Set(Field(outboxEvent => outboxEvent.NextAttemptAtUtc), nextAttemptAtUtc)
            .Unset(Field(outboxEvent => outboxEvent.ClaimedBy))
            .Unset(Field(outboxEvent => outboxEvent.ClaimedUntilUtc));
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

    // sent is false, eventType is one this relay publishes, the claim is missing or already over,
    // and the next attempt is missing or due. occurredAtUtc then _id is the order a pass publishes in.
    private static FilterDefinition<BsonDocument> DueUnsent(DateTime utcNow, IReadOnlyCollection<string> eventTypes)
    {
        var filter = Builders<BsonDocument>.Filter;
        var claimedUntilUtc = Field(outboxEvent => outboxEvent.ClaimedUntilUtc);
        var nextAttemptAtUtc = Field(outboxEvent => outboxEvent.NextAttemptAtUtc);
        return filter.And(
            filter.Eq(Field(outboxEvent => outboxEvent.Sent), false),
            filter.In(Field(outboxEvent => outboxEvent.EventType), eventTypes),
            filter.Or(filter.Exists(claimedUntilUtc, false), filter.Lte(claimedUntilUtc, utcNow)),
            filter.Or(filter.Exists(nextAttemptAtUtc, false), filter.Lte(nextAttemptAtUtc, utcNow)));
    }

    private static FilterDefinition<BsonDocument> HeldClaim(string eventId, string claimedBy)
    {
        var filter = Builders<BsonDocument>.Filter;
        return filter.And(
            filter.Eq(Field(outboxEvent => outboxEvent.Id), eventId),
            filter.Eq(Field(outboxEvent => outboxEvent.ClaimedBy), claimedBy),
            filter.Eq(Field(outboxEvent => outboxEvent.Sent), false));
    }

    // The collection is BsonDocument, so a property expression is not rendered for us.
    // The class map applies the same camel-case convention and BsonId as the documents we insert.
    internal static string Field<T>(Expression<Func<OutboxEvent, T>> property)
    {
        MongoConventions.Register();
        if (property.Body is not MemberExpression member)
        {
            throw new ArgumentException("The field must be an OutboxEvent property.", nameof(property));
        }

        var memberMap = BsonClassMap.LookupClassMap(typeof(OutboxEvent)).GetMemberMap(member.Member.Name);
        if (memberMap is null)
        {
            throw new InvalidOperationException($"OutboxEvent has no stored field {member.Member.Name}.");
        }

        return memberMap.ElementName;
    }

    private static void RequireUtc(DateTime value, string paramName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be UTC.", paramName);
        }
    }
}
