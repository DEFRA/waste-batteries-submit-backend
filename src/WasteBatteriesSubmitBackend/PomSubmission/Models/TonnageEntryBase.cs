using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Shared measure fields. The stored entry adds an id and sets activity.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record TonnageEntryBase
{
    public BatteryClassification Classification { get; init; } = new();

    // String decimal, for example "12.500". Not a JSON number.
    [BsonSerializer(typeof(StringDecimal128Serializer))]
    public string Tonnes { get; init; } = string.Empty;

    public TonnageEntry ToStoredEntry(string id, string activity) => new()
    {
        Id = id,
        Classification = Classification,
        Activity = activity,
        Tonnes = Tonnes
    };
}
