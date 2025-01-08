using MongoDB.Bson.Serialization.Attributes;

namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class PayloadModel
    {

        [BsonElement("action")]
        public string Action { get; set; }

        [BsonElement("collection")]
        public string Collection { get; set; }

        [BsonElement("document")]
        public Object Document { get; set; }

        [BsonElement("appId")]
        public string AppId { get; set; }

    }
}
