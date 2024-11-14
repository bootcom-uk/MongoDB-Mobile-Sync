using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDBDemoSync.Models
{
    public class AppSyncMapping
    {
        [BsonElement("_id")]
        public ObjectId Id { get; set; }

        [BsonElement("bearerToken")]
        public string BearerToken { get; set; }

        [BsonElement("version")]
        public int Version { get; set; }

        [BsonElement("fullRefreshIfNoActivityInDays")]
        public int FullRefreshIfNoActivityInDays { get; set; }

        [BsonElement("appName")]
        public string AppName { get; set; }

        [BsonElement("appId")]
        public string AppId { get; set; }

        [BsonElement("collections")]
        public List<CollectionMapping> Collections { get; set; }

        [BsonElement("endpoint")]
        public string Endpoint { get; set; }  // Endpoint for sending updates

        [BsonElement("hasInitialSyncComplete")]
        public bool HasInitialSyncComplete { get; set; }  // Flag for initial sync completion
    }
}
