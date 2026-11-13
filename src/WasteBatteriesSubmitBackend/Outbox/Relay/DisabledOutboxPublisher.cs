using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

[ExcludeFromCodeCoverage]
internal sealed class DisabledOutboxPublisher : IOutboxPublisher
{
    public Task PublishAsync(BsonDocument document, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The outbox relay is off.");
}
