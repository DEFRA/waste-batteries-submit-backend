using System.Text.Json;
using System.Text.Json.Serialization;

namespace WasteBatteriesSubmitBackend.PomSubmission.Models;

// batteryCategory is stored and returned. A posted value is rejected so the request cannot set it.
public sealed class BatteryCategoryJsonConverter : JsonConverter<string>
{
    // Null is still a posted value. The serializer would otherwise skip Read and clear the default.
    public override bool HandleNull => true;

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Skip();
        throw new JsonException("batteryCategory must not be supplied.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
