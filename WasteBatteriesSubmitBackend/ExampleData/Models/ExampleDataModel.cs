using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson.Serialization.Attributes;

namespace WasteBatteriesSubmitBackend.ExampleData.Models;

[ExcludeFromCodeCoverage]
[BsonIgnoreExtraElements]
public class ExampleDataModel
{
    [BsonId]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public required string ExampleText { get; set; }

    public required string UserId { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
