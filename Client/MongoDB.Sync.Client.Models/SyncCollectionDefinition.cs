using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Models
{
    public sealed class SyncCollectionDefinition
    {
        public Type ModelType { get; }

        public string CollectionName { get; }

        public string DatabaseName { get; set; }

        public SyncCollectionDefinition(Type modelType, string collectionName, string databaseName)
        {
            ModelType = modelType;
            CollectionName = collectionName;
            DatabaseName = databaseName;
        }
    }
}
