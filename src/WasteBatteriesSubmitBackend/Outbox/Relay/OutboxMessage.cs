using System.Globalization;
using System.Text.Json.Nodes;
using MongoDB.Bson;

namespace WasteBatteriesSubmitBackend.Outbox.Relay;

// The ADR-161 envelope. sent and sentAt stay in the outbox. So do the claim and retry fields.
internal static class OutboxMessage
{
    public const string EventIdAttribute = "eventId";

    public const string EventTypeAttribute = "eventType";

    private static readonly HashSet<string> Omitted = new(StringComparer.Ordinal)
    {
        "sent",
        "sentAtUtc",
        "expiresAtUtc",
        "claimedBy",
        "claimedUntilUtc",
        "attemptCount",
        "nextAttemptAtUtc"
    };

    public static string EventId(BsonDocument document) => RequiredString(document, "_id");

    public static string EventType(BsonDocument document) => RequiredString(document, "eventType");

    public static string CorrelationId(BsonDocument document) => RequiredString(document, "correlationId");

    public static string ToJson(BsonDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var json = new JsonObject
        {
            ["eventId"] = EventId(document),
            ["eventType"] = EventType(document),
            ["schemaVersion"] = RequiredInt(document, "schemaVersion"),
            ["subjectId"] = RequiredString(document, "subjectId"),
            ["version"] = RequiredLong(document, "version"),
            ["partitionKey"] = RequiredString(document, "partitionKey"),
            ["occurredAt"] = FormatUtc(RequiredDate(document, "occurredAtUtc")),
            ["actorId"] = RequiredString(document, "actorId"),
            ["correlationId"] = CorrelationId(document)
        };

        foreach (var element in document)
        {
            if (element.Name is "_id" or "occurredAtUtc"
                || Omitted.Contains(element.Name)
                || json.ContainsKey(element.Name))
            {
                continue;
            }

            json[element.Name] = ToNode(element.Value);
        }

        return json.ToJsonString();
    }

    private static JsonNode? ToNode(BsonValue value) => value.BsonType switch
    {
        BsonType.String => JsonValue.Create(value.AsString),
        BsonType.Boolean => JsonValue.Create(value.AsBoolean),
        BsonType.Int32 => JsonValue.Create(value.AsInt32),
        BsonType.Int64 => JsonValue.Create(value.AsInt64),
        BsonType.Double => JsonValue.Create(value.AsDouble),
        BsonType.Decimal128 => JsonValue.Create(value.AsDecimal128.ToString()),
        BsonType.ObjectId => JsonValue.Create(value.AsObjectId.ToString()),
        BsonType.DateTime => JsonValue.Create(FormatUtc(value.ToUniversalTime())),
        BsonType.Null => null,
        BsonType.Document => ToObject(value.AsBsonDocument),
        BsonType.Array => ToArray(value.AsBsonArray),
        _ => throw new InvalidOperationException($"Outbox field type {value.BsonType} cannot be published.")
    };

    private static JsonObject ToObject(BsonDocument document)
    {
        var json = new JsonObject();
        foreach (var element in document)
        {
            json[element.Name] = ToNode(element.Value);
        }

        return json;
    }

    private static JsonArray ToArray(BsonArray array)
    {
        var json = new JsonArray();
        foreach (var value in array)
        {
            json.Add(ToNode(value));
        }

        return json;
    }

    private static string RequiredString(BsonDocument document, string name)
    {
        if (!document.TryGetValue(name, out var value) || !value.IsString || string.IsNullOrWhiteSpace(value.AsString))
        {
            throw new InvalidOperationException($"Outbox document is missing {name}.");
        }

        return value.AsString;
    }

    private static int RequiredInt(BsonDocument document, string name)
    {
        if (!document.TryGetValue(name, out var value) || !value.IsInt32)
        {
            throw new InvalidOperationException($"Outbox document is missing {name}.");
        }

        return value.AsInt32;
    }

    private static long RequiredLong(BsonDocument document, string name)
    {
        if (!document.TryGetValue(name, out var value) || !value.IsInt64)
        {
            throw new InvalidOperationException($"Outbox document is missing {name}.");
        }

        return value.AsInt64;
    }

    private static DateTime RequiredDate(BsonDocument document, string name)
    {
        if (!document.TryGetValue(name, out var value) || !value.IsValidDateTime)
        {
            throw new InvalidOperationException($"Outbox document is missing {name}.");
        }

        return value.ToUniversalTime();
    }

    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
