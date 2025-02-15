using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

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
        public Object Document { get; set; }

        [JsonPropertyName("appId")]
        public string AppId { get; set; }

    }
}
