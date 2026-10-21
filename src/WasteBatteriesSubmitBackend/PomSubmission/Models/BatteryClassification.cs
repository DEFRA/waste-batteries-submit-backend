using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Embedded value object. It has no id of its own. Each dimension stores the
// reference-data text (for example "Portable", "Alkaline"), not the vocabulary id.
[ExcludeFromCodeCoverage]
[BsonIgnoreExtraElements]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BatteryClassification
{
    public string BatteryCategory { get; init; } = string.Empty;

    public string Chemistry { get; init; } = string.Empty;

    public string? SubCategory { get; init; }

    public string? CellType { get; init; }

    public string? EquipmentState { get; init; }
}
