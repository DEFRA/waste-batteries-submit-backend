using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using WasteBatteriesSubmitBackend.PomSubmission.Models;
using WasteBatteriesSubmitBackend.Utils.Mongo;

namespace WasteBatteriesSubmitBackend.Tests.Unit.Utils.Mongo;

public class StringDecimal128SerializerTests
{
    public StringDecimal128SerializerTests()
    {
        MongoConventions.Register();
    }

    [Fact]
    public void ToBsonDocument_WhenTonnesHasScale_RoundTripsAsDecimal128()
    {
        // Arrange
        var entry = new TonnageEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Classification = new BatteryClassification(),
            Activity = "Placed",
            Tonnes = "12.500"
        };

        // Act
        var document = entry.ToBsonDocument();
        var stored = BsonSerializer.Deserialize<TonnageEntry>(document);

        // Assert
        Assert.Equal(BsonType.Decimal128, document["tonnes"].BsonType);
        Assert.Equal("12.500", document["tonnes"].AsDecimal128.ToString());
        Assert.Equal("12.500", stored.Tonnes);
    }

    [Fact]
    public void Deserialize_WhenTonnesIsNotDecimal128_Throws()
    {
        // Arrange
        var document = new BsonDocument
        {
            { "id", ObjectId.GenerateNewId() },
            { "classification", new BsonDocument() },
            { "tonnes", "12.500" },
            { "activity", "Placed" }
        };

        // Act
        var act = () => BsonSerializer.Deserialize<TonnageEntry>(document);

        // Assert
        Assert.ThrowsAny<Exception>(act);
    }

    [Fact]
    public void ToBsonDocument_WhenTonnesIsNotADecimal_Throws()
    {
        // Arrange
        var entry = new TonnageEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Classification = new BatteryClassification(),
            Activity = "Placed",
            Tonnes = "twelve"
        };

        // Act
        var act = () => entry.ToBsonDocument();

        // Assert
        Assert.ThrowsAny<Exception>(act);
    }
}
