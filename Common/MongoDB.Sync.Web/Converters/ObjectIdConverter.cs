using MongoDB.Bson;
using MongoDB.Sync.Web.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Web.Converters
{
    public class ObjectIdConverter : JsonConverter<ObjectId>
    {
        public override ObjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                // Read inside the { "timestamp": ..., "creationTime": ... } structure
                using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("timestamp", out var timestamp))
                    {
                        return ObjectId.GenerateNewId(new DateTime(1970, 1, 1).AddSeconds(timestamp.GetInt64()));
                    }
                }
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                // Handle traditional ObjectId string format
                return ObjectId.Parse(reader.GetString());
            }

            throw new JsonException("Invalid ObjectId format.");
        }

        public override void Write(Utf8JsonWriter writer, ObjectId value, JsonSerializerOptions options)
        {
            var customId = new CustomId(value);
            writer.WriteRawValue(customId.ToString()); // Serialize as string instead of object
        }
    }
}
