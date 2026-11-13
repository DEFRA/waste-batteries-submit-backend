using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

public interface IOutboxPublisher
{
    Task PublishAsync(BsonDocument document, CancellationToken cancellationToken = default);
}
