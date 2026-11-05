using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using WasteBatteriesSubmitBackend.Utils.Mongo;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Posted measure. The stored entry adds an id, activity and batteryCategory.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record TonnageEntryBase<TClassification>
    where TClassification : BatteryClassificationBase, new()
{
    public TClassification Classification { get; init; } = new();

    // String decimal, for example "12.500". Not a JSON number.
    [BsonSerializer(typeof(StringDecimal128Serializer))]
    public string Tonnes { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record TonnageEntryBase : TonnageEntryBase<BatteryClassificationBase>
{
    public TonnageEntryBase Trimmed() => this with
    {
        Tonnes = StringValues.TrimToEmpty(Tonnes),
        Classification = Classification?.Trimmed()!
    };

    public TonnageEntry ToStoredEntry(string id, string activity, string batteryCategory) => new()
    {
        Id = id,
        Classification = new BatteryClassification
        {
            Chemistry = Classification?.Chemistry ?? string.Empty,
            BatteryCategory = batteryCategory
        },
        Activity = activity,
        Tonnes = Tonnes
    };
}
