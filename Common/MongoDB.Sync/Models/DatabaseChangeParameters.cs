using LiteDB;

namespace MongoDB.Sync.Models
{
    public class DatabaseChangeParameters
    {
        public required string CollectionName { get; set; }

        public BsonDocument? ChangedItem { get; set; }

        public ObjectId? Id { get; set; }

        public bool IsDeleted { get; set; }

    }
}
