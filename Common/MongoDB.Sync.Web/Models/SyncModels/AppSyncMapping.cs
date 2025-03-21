using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class AppSyncMapping
    {
        [BsonElement("_id")]
        public ObjectId Id { get; set; }

        [BsonElement("bearerToken")]
        public required string BearerToken { get; set; }

        [BsonElement("version")]
        public int Version { get; set; }

        [BsonElement("fullRefreshIfNoActivityInDays")]
        public int FullRefreshIfNoActivityInDays { get; set; }

        [BsonElement("appName")]
        public required string AppName { get; set; }

        [BsonElement("appDescription")]
        public string AppDescription { get; set; }

        [BsonElement("appId")]
        public required string AppId { get; set; }

        [BsonElement("collections")]
        public required List<CollectionMapping> Collections { get; set; }

        [BsonElement("endpoint")]
        public required string Endpoint { get; set; }  // Endpoint for sending updates

        [BsonElement("hasInitialSyncComplete")]
        public bool HasInitialSyncComplete { get; set; }  // Flag for initial sync completion

        public DateTime LastChecked { get; set; }
    }
}
