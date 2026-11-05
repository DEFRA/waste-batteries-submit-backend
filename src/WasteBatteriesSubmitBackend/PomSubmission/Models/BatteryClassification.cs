using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Stored classification. The mapper sets batteryCategory from PomSubmission:InitialBatteryCategory.
[BsonIgnoreExtraElements]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BatteryClassification : BatteryClassificationBase
{
    public string BatteryCategory { get; init; } = string.Empty;
}
