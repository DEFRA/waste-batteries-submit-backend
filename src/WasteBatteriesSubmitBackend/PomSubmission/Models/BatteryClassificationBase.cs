using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Chemistry posted on a tonnage entry. batteryCategory is not a request field.
[BsonIgnoreExtraElements]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record BatteryClassificationBase
{
    public string Chemistry { get; init; } = string.Empty;

    public BatteryClassificationBase Trimmed() => this with
    {
        Chemistry = StringValues.TrimToEmpty(Chemistry)
    };
}
