using LiteDB;

namespace MongoDB.Sync.Models
{
    public class UpdatedData
    {

            [BsonId]
            public ObjectId? Id { get; set; }

            public DateTime? UpdatedAt { get; set; }

            public string Document { get; set; } = string.Empty;

            public string Database { get; set; } = string.Empty;

            public string CollectionName {  get; set; } = string.Empty;

    }
}
