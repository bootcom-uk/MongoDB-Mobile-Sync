using MongoDB.Sync.Models.Attributes;

namespace MongoDB.Sync.Models
{
    [CollectionName("LocalCacheDataChange")]
    public abstract class LocalCacheDataChange
    {

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

    }
    
}
