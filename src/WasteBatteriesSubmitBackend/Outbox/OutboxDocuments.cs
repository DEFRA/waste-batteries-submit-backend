using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox;

// Writes an event with its runtime type, so a subclass's properties are stored beside the shared fields.
// The driver's discriminator is removed: eventType already says which event this is.
internal static class OutboxDocuments
{
    public static BsonDocument ToBsonDocument(OutboxEvent outboxEvent)
    {
        ArgumentNullException.ThrowIfNull(outboxEvent);

        var document = outboxEvent.ToBsonDocument();
        document.Remove("_t");
        return document;
    }
}
