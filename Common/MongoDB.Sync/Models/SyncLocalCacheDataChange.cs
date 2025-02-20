﻿using LiteDB;
using MongoDB.Sync.Models.Attributes;

namespace MongoDB.Sync.Models
{
    public class SyncLocalCacheDataChange : LocalCacheDataChange
    {

        /// <summary>
        /// Specifies the unique identifier of the change
        /// </summary>
        [DatabaseFieldName("_id")]
        public string Id { get; set; }


        /// <summary>
        /// Where the action is not a deletion, this contains the document of changes
        /// </summary>
        public BsonDocument? Document { get; set; }

        // Internal identifier
        public ObjectId InternalId { get; }  = ObjectId.NewObjectId();

    }
}
