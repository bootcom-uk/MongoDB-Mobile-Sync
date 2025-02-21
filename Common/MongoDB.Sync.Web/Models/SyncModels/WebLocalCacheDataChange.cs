using MongoDB.Bson;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;

namespace MongoDB.Sync.Web.Models.SyncModels
{
    public class WebLocalCacheDataChange : LocalCacheDataChange
    {

        /// <summary>
        /// Where the action is not a deletion, this contains the document of changes
        /// </summary>
        public BsonDocument? Document { get; set; }

    }
}
