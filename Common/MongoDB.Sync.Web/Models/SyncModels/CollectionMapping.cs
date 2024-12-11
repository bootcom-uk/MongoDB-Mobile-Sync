using MongoDB.Bson.Serialization.Attributes;

namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class CollectionMapping
    {
        [BsonElement("collectionName")]
        public required string CollectionName { get; set; }

        [BsonElement("databaseName")]
        public required string DatabaseName { get; set; }

        [BsonElement("fields")]
        public List<string>? Fields { get; set; }

        [BsonElement("version")]
        public int Version { get; set; }

    }
}
