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
    public void Tonnes_round_trips_as_decimal128_and_keeps_scale()
    {
        var entry = new TonnageEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Classification = new BatteryClassification(),
            Activity = "Placed",
            Tonnes = "12.500"
        };

        var document = entry.ToBsonDocument();

        Assert.Equal(BsonType.Decimal128, document["tonnes"].BsonType);
        Assert.Equal("12.500", document["tonnes"].AsDecimal128.ToString());
        Assert.Equal("12.500", BsonSerializer.Deserialize<TonnageEntry>(document).Tonnes);
    }

    [Fact]
    public void Tonnes_rejects_a_value_that_is_not_a_decimal()
    {
        var entry = new TonnageEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Classification = new BatteryClassification(),
            Activity = "Placed",
            Tonnes = "twelve"
        };

        Assert.ThrowsAny<Exception>(() => entry.ToBsonDocument());
    }
}
