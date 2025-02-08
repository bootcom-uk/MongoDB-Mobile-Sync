using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class PayloadModel
    {

        [BsonElement("action")]
        [JsonPropertyName("action")]
        public string Action { get; set; }

        [BsonElement("collection")]
        [JsonPropertyName("collection")]
        public string Collection { get; set; }

        [BsonElement("database")]
        [JsonPropertyName("database")]
        public string Database { get; set; }

        [BsonElement("document")]
        [JsonPropertyName("document")]
        public Object Document { get; set; }

        [BsonElement("appId")]
        [JsonPropertyName("appId")]
        public string AppId { get; set; }

    }
}
