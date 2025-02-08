using LiteDB;
using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using System.Collections;
using System;

namespace MongoDB.Sync.Services
{
    public class LocalDatabaseService
    {

        private readonly IMessenger _messenger;

        private readonly LiteDatabase _liteDb;

        private string? _appName { get; set; }

        public LocalDatabaseService(IMessenger messenger, string liteDbPath) { 
            _liteDb = new LiteDatabase(liteDbPath);
            _messenger = messenger;

            _messenger.Register<RealtimeUpdateReceivedMessage>(this, HandleRealtimeUpdate);
            _messenger.Register<InitializeLocalDataMappingMessage>(this, HandleLocalDataMappings);
            _messenger.Register<ClearLocalCacheMessage>(this, ClearLocalCache);

        }

        private void ClearLocalCache(object recipient, ClearLocalCacheMessage message)
        {
            foreach (var collectionName in _liteDb.GetCollectionNames())
            {
                _liteDb.DropCollection(collectionName);
            }

            if (message != null && message.Value)
            {
                _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            }
        }

        public AppSyncMapping GetAppMapping()
        {
            var appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            return appsCollection.FindOne(x => x.AppName == _appName);
        }

        private void HandleLocalDataMappings(object recipient, InitializeLocalDataMappingMessage message)
        {
            var mapping = message.Value;

            _appName = mapping.AppName;

            var appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == mapping.AppName);

            // Our mappings haven't ever been entered so we now need to add these to the 
            // local database
            if(appRecord is null)
            {
                appsCollection.Insert(mapping);
                return;
            }

            // The mappings record exists so lets see if there are any changes between the original 
            // record and the new one.
            if(appRecord.Version != mapping.Version)
            {
                // Where the mapping version has been modified this means we need to clear out 
                // our entire cache and rebuild
                ClearLocalCache(this, new ClearLocalCacheMessage(false));

                appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");

                appsCollection.Insert(mapping);

                return;
            }

            // If we have our mappings but we haven't updated the app in a 
            // set amount of time then we need to regenerate the cache
            // if(appRecord.FullRefreshIfNoActivityInDays == last

        }

        public DateTime? GetLastSyncDateTime(string databaseName, string collectionName)
        {            
            var collection = _liteDb.GetCollection($"{databaseName}_{collectionName}".Replace("-", "_"));
            var lastDoc = collection
            .Find(Query.All("__meta.dateUpdated", Query.Descending))
            .FirstOrDefault();

            if (lastDoc != null && lastDoc["__meta"]["dateUpdated"].IsDateTime)
            {
                return lastDoc["__meta"]["dateUpdated"].AsDateTime;                
            }

            return null;
        }

        public ObjectId? GetLastId(string databaseName, string collectionName)
        {
            var appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            var collectionToModify = appRecord.Collections.FirstOrDefault(record => record.DatabaseName == databaseName && record.CollectionName == collectionName);
            if (collectionToModify == null) return null;
            return collectionToModify.LastId;
        }

        public void InitialSyncComplete()
        {
            var appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            appRecord.InitialSyncComplete = true;
            appsCollection.Update(appRecord);
        }

        private void SetLastId(string databaseName, string collectionName, ObjectId id)
        {
            var appsCollection = _liteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            var collectionToModify = appRecord.Collections.FirstOrDefault(record => record.DatabaseName == databaseName &&  record.CollectionName == collectionName);
            if (collectionToModify == null) return;
            collectionToModify.LastId = id;
            appsCollection.Update(appRecord);
        }

        private void HandleRealtimeUpdate(object recipient, RealtimeUpdateReceivedMessage message)
        {

            try
            {
                var updateData = message.Value;

                var doc = LiteDB.JsonSerializer.Deserialize(updateData.Document).AsDocument;

                if (doc is null) return;

                var collectionName = $"{updateData.Database}_{updateData.CollectionName}";
                collectionName = collectionName.Replace("-", "_");

                var collection = _liteDb.GetCollection(collectionName);

                // If the document has been removed at the server level then clear out 
                // of our local cache
                if (doc.TryGetValue("__meta", out var metaField) &&
                metaField.AsDocument.TryGetValue("deleted", out var isDeleted))
                {
                    if (isDeleted.AsBoolean)
                    {
                        if (doc.TryGetValue("_id", out var docId))
                        {
                            collection.Delete(docId);
                            return;
                        }
                    }
                }

                // Insert/update document
                collection.Upsert(doc);

                // Update the last id in the database
                doc.TryGetValue("_id", out var lastId);
                SetLastId(updateData.Database, updateData.CollectionName, lastId.AsObjectId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            

        }


    }
}
