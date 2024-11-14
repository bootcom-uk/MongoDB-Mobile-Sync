using MongoDB.Bson.Serialization.Attributes;

namespace MongoDBDemoSync.Models
{
    public class CollectionMapping
    {
        [BsonElement("collectionName")]
        public string CollectionName { get; set; }

        [BsonElement("databaseName")]
        public string DatabaseName { get; set; }  // Name of the database the collection belongs to

        [BsonElement("fields")]
        public List<string> Fields { get; set; }

        [BsonElement("version")]
        public int Version { get; set; }

    }
}
