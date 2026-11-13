using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Options;
using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

public sealed class SnsOutboxPublisher(
    IAmazonSimpleNotificationService simpleNotificationService,
    IOptions<OutboxRelayOptions> options) : IOutboxPublisher
{
    public Task PublishAsync(BsonDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var eventId = OutboxMessage.EventId(document);
        var eventType = OutboxMessage.EventType(document);
        var request = new PublishRequest
        {
            TopicArn = options.Value.TopicArn,
            Message = OutboxMessage.ToJson(document),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [OutboxMessage.EventIdAttribute] = Attribute(eventId),
                [OutboxMessage.EventTypeAttribute] = Attribute(eventType)
            }
        };

        return simpleNotificationService.PublishAsync(request, cancellationToken);
    }

    private static MessageAttributeValue Attribute(string value) =>
        new()
        {
            DataType = "String",
            StringValue = value
        };
}
