using MongoDB.Bson;
using MongoDB.Sync.Web.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Web.Converters
{
    // Custom converter to deserialize `id` field properly
    public class ObjectIdConverter : JsonConverter<ObjectId>
    {
        public override ObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                // Handle traditional ObjectId string format
                return ObjectId.Parse(reader.GetString());
            }

            throw new JsonException("Invalid ObjectId format.");
        }

        public override void Write(Utf8JsonWriter writer, ObjectId value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString()); // Serialize as string instead of object
        }
    }
}
