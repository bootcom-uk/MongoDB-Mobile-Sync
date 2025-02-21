using LiteDB;
using MongoDB.Sync.Models.Attributes;
using System.Text.Json.Serialization;

namespace MongoDB.Sync.Models
{
    public class SyncLocalCacheDataChange : LocalCacheDataChange
    {

        /// <summary>
        /// Where the action is not a deletion, this contains the document of changes
        /// </summary>
        public BsonDocument? Document { get; set; }

        // Internal identifier
        public ObjectId InternalId { get; }  = ObjectId.NewObjectId();

    }
}
