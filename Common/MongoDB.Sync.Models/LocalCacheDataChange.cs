using MongoDB.Sync.Models.Attributes;

namespace MongoDB.Sync.Models
{
    [CollectionName("LocalCacheDataChange")]
    public abstract class LocalCacheDataChange
    {

        /// <summary>
        /// Specifies the unique identifier of the change
        /// </summary>
        [DatabaseFieldName("_id")]
        public ObjectId Id { get; set; }

        /// <summary>
        /// Specifies the timestamp of the change
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Specifies the collection name where the change was made
        /// </summary>
        public required string CollectionName { get; set; }

        /// <summary>
        /// Specifies whether this was the removal of a record
        /// </summary>
        public bool IsDeletion { get; set; } = false;

        /// <summary>
        /// Where the action is not a deletion, this contains the document of changes
        /// </summary>
        public BsonDocument? Document { get; set; }
    }
    
}
