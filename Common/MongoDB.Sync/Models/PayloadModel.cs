using System.Text.Json;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Models
{
    public class PayloadModel
    {

        [JsonPropertyName("action")]
        public string Action { get; set; }

        [JsonPropertyName("collection")]
        public string Collection { get; set; }

        [JsonPropertyName("database")]
        public string Database { get; set; }

        [JsonPropertyName("document")]
        public JsonElement Document { get; set; }

        [JsonPropertyName("appId")]
        public string AppId { get; set; }

    }
}
