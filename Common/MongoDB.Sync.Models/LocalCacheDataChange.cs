using MongoDB.Sync.Models.Attributes;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Models
{
    [CollectionName("LocalCacheDataChange")]
    public class LocalCacheDataChange
    {

        /// <summary>
        /// Specifies the unique identifier of the change
        /// </summary>
        [DatabaseFieldName("_id")]
        public required string Id { get; set; }

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
        /// Specifies the serialized document that was changed
        /// </summary>
        public string? SerializedDocument { get; set; }

    }
    
}
