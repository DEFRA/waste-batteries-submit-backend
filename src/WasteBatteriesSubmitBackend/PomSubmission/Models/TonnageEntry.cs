using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// One stored measure. Id, activity and batteryCategory are set by the service.
// BsonNoId keeps Id as the element "id". Only the submission document uses _id.
[BsonIgnoreExtraElements]
[BsonNoId]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TonnageEntry : TonnageEntryBase<BatteryClassification>
{
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; init; }

    public string Activity { get; init; } = string.Empty;
}
