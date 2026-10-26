using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using WasteBatteriesSubmitBackend.Utils.Text;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// Embedded value object. It has no id of its own. Each dimension stores the
// reference-data text (for example "Portable", "Alkaline"), not the id.
// batteryCategory is required on the stored classification. The request JSON
// must not include it. Every submission stores Portable.
[ExcludeFromCodeCoverage]
[BsonIgnoreExtraElements]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BatteryClassification
{
    public const string Portable = "Portable";

    [JsonConverter(typeof(BatteryCategoryJsonConverter))]
    public string BatteryCategory { get; init; } = Portable;

    public string Chemistry { get; init; } = string.Empty;

    public BatteryClassification Trimmed() => this with
    {
        BatteryCategory = Portable,
        Chemistry = StringValues.TrimToEmpty(Chemistry)
    };
}
