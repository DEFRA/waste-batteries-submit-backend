using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace WasteBatteriesSubmitBackend.Utils.Mongo;

// Tonnages are a string at the API and Decimal128 at rest, so JSON never builds a float.
// The string is parsed only at this boundary.
public sealed class StringDecimal128Serializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var bsonType = context.Reader.GetCurrentBsonType();
        if (bsonType != BsonType.Decimal128)
        {
            throw CreateCannotDeserializeFromBsonTypeException(bsonType);
        }

        return context.Reader.ReadDecimal128().ToString();
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {
        context.Writer.WriteDecimal128(Decimal128.Parse(value));
    }
}
