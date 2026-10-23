using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Embedded value object. It has no id of its own. Each dimension stores the
// reference-data text (for example "Portable", "Alkaline"), not the id.
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

    public BatteryClassification Trimmed() => this with
    {
        BatteryCategory = StringValues.TrimToEmpty(BatteryCategory),
        Chemistry = StringValues.TrimToEmpty(Chemistry),
        // Omitted, null, empty and whitespace are the same absent dimension.
        SubCategory = StringValues.TrimToNull(SubCategory),
        CellType = StringValues.TrimToNull(CellType),
        EquipmentState = StringValues.TrimToNull(EquipmentState)
    };
}
