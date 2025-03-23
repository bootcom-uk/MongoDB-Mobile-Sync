using MongoDB.Bson;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Web.Models
{
    public class CustomId
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("creationTime")]
        public string CreationTime { get; set; }

        public CustomId(ObjectId objectId)
        {
            Timestamp = objectId.Timestamp;
            CreationTime = objectId.CreationTime.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

    }
}
