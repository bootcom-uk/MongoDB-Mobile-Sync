using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Converters;
using MongoDB.Sync.Core.Services.Models.Services;
using MongoDB.Sync.LiteDb;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using System;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace MongoDB.Sync.Services
{
    public class LocalDatabaseSyncService
    {

        private readonly IMessenger _messenger;

        public readonly LiteDatabase LiteDb;

        public string? _appName { get; set; }

        private readonly BaseTypeResolverService _baseTypeResolverService;

        private readonly LiteDbQueueProcessor _liteDbQueueProcessor;

        public LocalDatabaseSyncService(IMessenger messenger, string liteDbPath, BaseTypeResolverService baseTypeResolverService) {
            LiteDb = new LiteDatabase(liteDbPath);
            _liteDbQueueProcessor = new(LiteDb);

            _messenger = messenger;
            _baseTypeResolverService = baseTypeResolverService;

            _messenger.Register<RealtimeUpdateReceivedMessage>(this, HandleRealtimeUpdate);
            _messenger.Register<APISyncMessageReceived>(this, HandleAPISyncMessageReceived);
            _messenger.Register<InitializeLocalDataMappingMessage>(this, HandleLocalDataMappings);
            _messenger.Register<ClearLocalCacheMessage>(this, ClearLocalCache);

        }

        

        private void ClearLocalCache(object recipient, ClearLocalCacheMessage message)
        {
            foreach (var collectionName in LiteDb.GetCollectionNames())
            {
                LiteDb.DropCollection(collectionName);
            }

            if (message != null && message.Value)
            {
                LiteDb.GetCollection<AppSyncMapping>("AppMappings");
            }
        }

        public void ClearCollection(string collectionName)
        {
            LiteDb.DropCollection(collectionName);
        }

        public AppSyncMapping GetAppMapping()
        {
            var appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");
            return appsCollection.FindOne(x => x.AppName == _appName);
        }

        private void HandleLocalDataMappings(object recipient, InitializeLocalDataMappingMessage message)
        {
            var mapping = message.Value;

            _appName = mapping.AppName;

            var appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");

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

                // The mapping version has been updated so we need to see whether any of the collections have updated
                foreach (var collection in appRecord.Collections)
                {
                    var newCollection = mapping.Collections.FirstOrDefault(x => x.DatabaseName == collection.DatabaseName && x.CollectionName == collection.CollectionName);

                    // This collection doesn't exist in the new mapping or the version is different. Either way
                    // we need to remove the collection from the local cache
                    if (newCollection == null || collection.Version != newCollection.Version)
                    {
                        LiteDb.DropCollection(collection.CollectionName);
                    }
                }

                appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");

                appsCollection.Upsert(mapping);

                return;
            }

            // If we have our mappings but we haven't updated the app in a 
            // set amount of time then we need to regenerate the cache
             if(appRecord != null && appRecord.ServerDateTime != null && mapping.ServerDateTime!.Value.Subtract(appRecord.ServerDateTime.Value).Days > mapping.FullRefreshIfNoActivityInDays)
            {
                // Where we haven't sync'd data in a while then we need to clear out the cache
                ClearLocalCache(this, new ClearLocalCacheMessage(false));
            }

             if(appRecord == null) return;

            appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");            
            appRecord.ServerDateTime = mapping.ServerDateTime;
            appsCollection.Upsert(mapping);

        }

        public DateTime? GetLastSyncDateTime(string databaseName, string collectionName)
        {
            try
            {
                var collection = LiteDb.GetCollection($"{databaseName}_{collectionName}".Replace("-", "_"));
                var lastDoc = collection
                .Find(Query.All("__meta.dateUpdated", Query.Descending))
                .FirstOrDefault();

                if (lastDoc != null && lastDoc["__meta"]["dateUpdated"].IsDateTime)
                {
                    return lastDoc["__meta"]["dateUpdated"].AsDateTime;
                }
            }
            catch(Exception ex)
            {
                var collection = LiteDb.GetCollection($"{databaseName}_{collectionName}".Replace("-", "_"));
                var lastDoc = collection
                .Find(Query.All("__meta.dateUpdated", Query.Descending))
                .FirstOrDefault();
                Console.WriteLine(collectionName);
            }
            
            return null;
        }

        public ObjectId? GetLastId(string databaseName, string collectionName)
        {
            var appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            var collectionToModify = appRecord.Collections.FirstOrDefault(record => record.DatabaseName == databaseName && record.CollectionName == collectionName);
            if (collectionToModify == null) return null;
            return collectionToModify.LastId;
        }

        public void InitialSyncComplete()
        {
            var appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            appRecord.InitialSyncComplete = true;
            appsCollection.Update(appRecord);
        }

        private void SetLastId(string databaseName, string collectionName, ObjectId id)
        {
            var appsCollection = LiteDb.GetCollection<AppSyncMapping>("AppMappings");
            var appRecord = appsCollection.FindOne(x => x.AppName == _appName);
            var collectionToModify = appRecord.Collections.FirstOrDefault(record => record.DatabaseName == databaseName &&  record.CollectionName == collectionName);
            if (collectionToModify == null) return;
            collectionToModify.LastId = id;
            appsCollection.Update(appRecord);
        }

        private void HandleAPISyncMessageReceived(object recipient, APISyncMessageReceived message)
        {
            _liteDbQueueProcessor.Enqueue(async db =>
            {
                var updateData = System.Text.Json.JsonSerializer.Deserialize<UpdatedData>(message.Value);
                if (updateData is null || updateData.Document is null) return;

                var jsonDoc = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(updateData.Document);
                var doc = EjsonConverter.NormalizeEjson(jsonDoc);
                if (doc is null) return;

                var collectionName = $"{updateData.Database}_{updateData.CollectionName}".Replace("-", "_");
                var collection = db.GetCollection(collectionName);

                if (!doc.TryGetValue("_id", out var docId)) return;


                BsonValue? isDeleted = new BsonValue(false);

                doc.TryGetValue("__meta", out var metaField);
                metaField?.AsDocument.TryGetValue("deleted", out isDeleted);

                if (isDeleted is not null && isDeleted.AsBoolean)
                {
                    collection.Delete(docId);
                }
                else
                {
                    collection.Upsert(doc);
                }

                _messenger.Send(new DatabaseChangeMessage(new()
                {
                    ChangedItem = doc,
                    IsDeleted = isDeleted ?? new BsonValue(false),
                    CollectionName = collectionName,
                    Id = docId.AsObjectId
                }));

                SetLastId(updateData.Database, updateData.CollectionName, docId.AsObjectId);
            });
        }


        private void HandleRealtimeUpdate(object recipient, RealtimeUpdateReceivedMessage message)
        {
            try
            {

                var updateData = System.Text.Json.JsonSerializer.Deserialize<PayloadModel>(message.Value);
                if(updateData is null) return;

                var collectionName = $"{updateData.Database}_{updateData.Collection}".Replace("-", "_");

                var outputType = _baseTypeResolverService.CollectionNameToType(collectionName);

                if (outputType is null) return;

                var doc = EjsonConverter.NormalizeEjson(updateData.Document);

                // You can now use this with LiteDB:
                if (doc is null) return;

                var collection = LiteDb.GetCollection(collectionName);

                // If the document doesn't contain an ID, we can't do anything with it
                if (!doc.TryGetValue("_id", out var documentId))
                {
                    return;
                }

                BsonDocument existingDoc;    

                switch (updateData.Action)
                {
                    case "insert":

                        existingDoc = collection.FindOne(record => record["_id"].AsObjectId == documentId.AsObjectId);
                        if (existingDoc != null) return;

                        collection.Insert(doc);

                        _messenger.Send(new DatabaseChangeMessage(new()
                        {
                            ChangedItem = doc,
                            CollectionName = collectionName,
                            Id = documentId
                        }));
                        break;
                    case "update":
                        existingDoc = collection.FindOne(record => record["_id"].AsObjectId == documentId.AsObjectId);
                        if (existingDoc == null) return;
                        // Merge the update into the existing document
                        foreach (var kvp in doc)
                        {
                            if (kvp.Key == "_id") continue;
                            existingDoc[kvp.Key] = kvp.Value;
                        }

                        //Save the merged document
                        var updated = collection.Update(existingDoc);
                                               

                        _messenger.Send(new DatabaseChangeMessage(new()
                        {
                            ChangedItem = existingDoc,
                            CollectionName = collectionName,
                            Id = documentId
                        }));
                        break;
                    case "delete":
                        var deleted = collection.Delete(documentId);

                        _messenger.Send(new DatabaseChangeMessage(new()
                        {
                            ChangedItem = null,
                            IsDeleted = true,
                            CollectionName = collectionName,
                            Id = documentId
                        }));
                        break;
                    default:
                        return;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }




    }
}
