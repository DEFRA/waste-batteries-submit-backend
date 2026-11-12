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
        return Collection.Database
            .GetCollection<BsonDocument>(CollectionName)
            .InsertManyAsync(session, documents, cancellationToken: cancellationToken);
    }

    protected override List<CreateIndexModel<OutboxEvent>> DefineIndexes(IndexKeysDefinitionBuilder<OutboxEvent> builder) =>
        OutboxIndexes.Create(builder);
}
