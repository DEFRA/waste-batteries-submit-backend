using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// One measure on a submission: which kind of battery, what happened to it, and how much.
// Embedded in the submission, so this does not repeat submissionId.
// BsonNoId keeps Id as the element "id". Only the submission document uses _id.
[ExcludeFromCodeCoverage]
[BsonIgnoreExtraElements]
[BsonNoId]
public sealed record TonnageEntry : TonnageEntryBase
{
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; init; }

    public string Activity { get; init; } = string.Empty;
}
