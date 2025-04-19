using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using MongoDB.Sync.Converters;
using MongoDB.Sync.Core.Services.Models.Services;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Attributes;
using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json.Nodes;

namespace MongoDB.Sync.Services
{
    public class LocalDatabaseSyncService
    {

        private readonly IMessenger _messenger;

        public readonly LiteDatabase LiteDb;

        private string? _appName { get; set; }

        private readonly BaseTypeResolverService _baseTypeResolverService;

        public LocalDatabaseSyncService(IMessenger messenger, string liteDbPath, BaseTypeResolverService baseTypeResolverService) {
            LiteDb = new LiteDatabase(liteDbPath);
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

        }

        public DateTime? GetLastSyncDateTime(string databaseName, string collectionName)
        {            
            var collection = LiteDb.GetCollection($"{databaseName}_{collectionName}".Replace("-", "_"));
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

        private void HandleAPISyncMessageReceived(object recipient, APISyncMessageReceived message) {
            var updateData = System.Text.Json.JsonSerializer.Deserialize<UpdatedData>(message.Value);

            if (updateData is null || updateData.Document is null) return;

            var doc = LiteDB.JsonSerializer.Deserialize(updateData.Document.ToString()).AsDocument;

            if (doc is null) return;

            var collectionName = $"{updateData.Database}_{updateData.CollectionName}";
            collectionName = collectionName.Replace("-", "_");

            var collection = LiteDb.GetCollection(collectionName);

            BsonValue? docId;
            BsonValue? isDeleted = null;

            if (!doc.TryGetValue("_id", out docId))
            {
                return;
            }

            doc.TryGetValue("__meta", out var metaField);
            metaField.AsDocument.TryGetValue("deleted", out isDeleted);

            // If the document has been removed at the server level then clear out 
            // of our local cache
            if (isDeleted != null && isDeleted.AsBoolean)
            {
                collection.Delete(docId);
                _messenger.Send(new DatabaseChangeMessage(new()
                {
                    ChangedItem = doc,
                    IsDeleted = isDeleted,
                    CollectionName = collectionName,
                    Id = docId.AsObjectId
                }));
                return;
            }

            // Insert/update document
            collection.Upsert(doc);

            if(isDeleted is null)
            {
                isDeleted = new BsonValue(false);
            }

            _messenger.Send(new DatabaseChangeMessage(new()
            {
                ChangedItem = doc,
                IsDeleted = isDeleted,
                CollectionName = collectionName,
                Id = docId.AsObjectId
            }));

            // Update the last id in the database           
            SetLastId(updateData.Database, updateData.CollectionName, docId.AsObjectId);
        }

        private void HandleRealtimeUpdate(object recipient, RealtimeUpdateReceivedMessage message)
        {
            try
            {
               // var updateData = System.Text.Json.JsonSerializer.Deserialize<PayloadModel>(message.Value);

               // var doc = LiteDB.JsonSerializer.Deserialize(updateData!.Document.GetRawText()).AsDocument;


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

                        // var typedObj = System.Text.Json.JsonSerializer.Deserialize(normalizedJson, outputType);

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

                // New code end

               // var updateDoc = MongoJsonConverter.ConvertMongoJsonToLiteDB(docJson);



               // if (updateDoc is null) return;

               // var collectionName = $"{updateData.Database}_{updateData.Collection}".Replace("-", "_");
               // var collection = LiteDb.GetCollection(collectionName);

               // If the document doesn't contain an ID, we can't do anything with it
               // if (!updateDoc.TryGetValue("_id", out var documentId))
               // {
               //     return;
               // }

               // Check if the update document contains metadata for deletion
               // if (updateDoc.TryGetValue("deleted", out var isDeleted) && isDeleted.AsBoolean)
               //     {

               //         If the doc is marked as deleted, remove it from the local cache
               //    var deleted = collection.Delete(documentId);

               //         if (isDeleted is null)
               //         {
               //             isDeleted = new BsonValue(false);
               //         }

               //         _messenger.Send(new DatabaseChangeMessage(new()
               //         {
               //             ChangedItem = null,
               //             IsDeleted = isDeleted,
               //             CollectionName = collectionName,
               //             Id = documentId
               //         }));

               //         return;
               //     }

               // Try to fetch the existing document from LiteDB
               //var existingDoc = collection.FindOne(record => record["_id"].AsObjectId == documentId.AsObjectId);
               // if (existingDoc != null)
               // {
               //     Merge the update into the existing document
               //     foreach (var kvp in updateDoc)
               //     {
               //         if (kvp.Key == "_id") continue;
               //         existingDoc[kvp.Key] = kvp.Value;
               //     }

               //     Save the merged document
               //    var updated = collection.Update(existingDoc);


               // }
               // else
               // {
               //     If the document doesn't exist, insert the update as a new document
               //     collection.Insert(updateDoc);

               //     Update the last processed ID
               //     SetLastId(updateData.Database, updateData.Collection, documentId.AsObjectId);

               // }

               // _messenger.Send(new DatabaseChangeMessage(new()
               // {
               //     ChangedItem = updateDoc,
               //     IsDeleted = false,
               //     CollectionName = collectionName,
               //     Id = documentId
               // }));

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }




    }
}
