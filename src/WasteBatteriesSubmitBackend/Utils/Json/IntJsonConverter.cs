using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.Utils.Json;

// Pass the JSON property name. Null or a blank string is read as 0 so validation
// can reject the field. Any other non-number fails the body and names that property.
[AttributeUsage(AttributeTargets.Property)]
public sealed class IntJsonConverterAttribute(string propertyName) : JsonConverterAttribute
{
    public override JsonConverter CreateConverter(Type typeToConvert) => new IntJsonConverter(propertyName);
}

public sealed class IntJsonConverter(string propertyName) : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return 0;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return 0;
                }

                if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                break;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return number;
        }

        throw new JsonException($"{propertyName} must be a JSON number.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
