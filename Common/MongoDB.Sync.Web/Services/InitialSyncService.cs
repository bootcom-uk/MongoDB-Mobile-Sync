﻿using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Sync.Web.Models.SyncModels;

namespace MongoDB.Sync.Web.Services
{
    public class InitialSyncService
    {

        private readonly IMongoDatabase _appServicesDb;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<InitialSyncService> _logger;

        public InitialSyncService(IMongoClient mongoClient, IHttpClientFactory httpClientFactory, ILogger<InitialSyncService> logger)
        {
            _appServicesDb = mongoClient.GetDatabase("AppServices");
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> HasInitialSyncCompleted(string appId)
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");
            var appMapping = await appCollection.Find(a => a.AppId == appId).FirstOrDefaultAsync();
            if (appMapping == null)
            {
                return false;
            }

            return appMapping.HasInitialSyncComplete;
        }

        public async Task<bool> PerformInitialSync(string appName, AppSyncMapping? originalMapping)
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");

            // Get app mapping from SyncMappings collection
            var appMapping = await appCollection.Find(a => a.AppName == appName).FirstOrDefaultAsync();
            if (appMapping == null)
            {
                _logger.LogWarning($"App with name {appName} not found in SyncMappings.");
                return false;
            }

            if (appMapping.HasInitialSyncComplete)
            {
                _logger.LogInformation($"Initial sync for app {appName} has already been completed.");
                return false;
            }

            // Sync each collection defined in the app's mapping
            foreach (var collectionMapping in appMapping.Collections)
            {
                // Check if the collection mapping has been updated since the last sync, if it has then perform a full sync
                if (originalMapping != null)
                {
                   var originalMappingCollection = originalMapping.Collections.FirstOrDefault(c => c.CollectionName == collectionMapping.CollectionName);
                    if(originalMappingCollection != null && originalMappingCollection.Version != collectionMapping.Version)
                    {
                        // Collection details not updated so let's now move to the next collection
                        continue;
                    }
                }

                await SyncCollection(appMapping, collectionMapping);
                
            }

            // After successful sync of all collections, set flag to true
            var updateDefinition = Builders<AppSyncMapping>.Update.Set(a => a.HasInitialSyncComplete, true);
            await appCollection.UpdateOneAsync(a => a.AppName == appName, updateDefinition);

            _logger.LogInformation($"Initial sync for app {appName} completed successfully.");
            return true;
        }

        private async Task SyncCollection(AppSyncMapping appMapping, CollectionMapping collectionMapping)
        {
            var collectionName = collectionMapping.CollectionName;
            var dbName = collectionMapping.DatabaseName;

            // Get source database and collection
            var sourceDb = _appServicesDb.Client.GetDatabase(dbName);
            var sourceCollection = sourceDb.GetCollection<BsonDocument>(collectionName);

            // Get or create target collection in the AppServices database
            var targetDb = _appServicesDb.Client.GetDatabase("AppServices");
            var targetCollectionName = $"{appMapping.AppId}_{collectionName}";

            // Check if target collection exists - if it does, drop it 
            var existingCollections = await targetDb.ListCollectionNames().ToListAsync();
            if (existingCollections.Contains(targetCollectionName))
            {
                await targetDb.DropCollectionAsync(targetCollectionName);                
            }

            await targetDb.CreateCollectionAsync(targetCollectionName);
            _logger.LogInformation($"Created collection {targetCollectionName} in AppServices database.");

            var targetCollection = targetDb.GetCollection<BsonDocument>(targetCollectionName);


            // Create index on __meta.dateUpdated
            var indexKeys = Builders<BsonDocument>.IndexKeys.Ascending("__meta.dateUpdated");
            var indexModel = new CreateIndexModel<BsonDocument>(indexKeys);
            await targetCollection.Indexes.CreateOneAsync(indexModel);

            _logger.LogInformation($"Created index on __meta.dateUpdated in collection {targetCollectionName}.");

            _logger.LogInformation($"Starting sync for collection {collectionName} in database {dbName} for app {appMapping.AppId}.");

            // Paginate the documents to avoid memory overflow during large syncs
            var batchSize = 1000;
            var totalDocuments = await sourceCollection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            var totalBatches = (int)Math.Ceiling(totalDocuments / (double)batchSize);

            for (var batch = 0; batch < totalBatches; batch++)
            {
                var documents = await sourceCollection
                    .Find(Builders<BsonDocument>.Filter.Empty)
                    .Skip(batch * batchSize)
                    .SortByDescending(record => record["_id"]) // Ensure consistent ordering
                    .Limit(batchSize)
                    .ToListAsync();

                var filteredDocuments = new List<BsonDocument>();

                foreach (var doc in documents)
                {
                    var filteredDocument = new BsonDocument();

                    if (collectionMapping.Fields is not null)
                    {
                        foreach (var field in collectionMapping.Fields)
                        {
                            if (doc.Contains(field))
                            {
                                filteredDocument[field] = doc[field];
                            }
                        }
                    }

                    filteredDocument["__meta"] = new BsonDocument { { "dateUpdated", DateTime.UtcNow } };
                    filteredDocuments.Add(filteredDocument);
                }

                // Insert documents into the target collection
                if (filteredDocuments.Any())
                {
                    await targetCollection.InsertManyAsync(filteredDocuments);
                }
            }

            _logger.LogInformation($"Sync completed for collection {collectionName} in database {dbName}.");
            
        }

    }
}
