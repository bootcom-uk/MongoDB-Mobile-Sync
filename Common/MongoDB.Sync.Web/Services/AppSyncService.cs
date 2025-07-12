using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Sync.Models.Web;
using MongoDB.Sync.Web.Interfaces;
using MongoDB.Sync.Web.Models.SyncModels;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MongoDB.Sync.Web.Services
{
    public class AppSyncService : IAppSyncService
    {
        private readonly IMongoDatabase _appServicesDb;
        private readonly ILogger<AppSyncService> _logger;
        private const int BatchSize = 100; // Define your batch size
        private readonly IMongoClient _mongoClient;
        private readonly InitialSyncService _initialSyncService;

        public AppSyncService(IMongoClient mongoClient, ILogger<AppSyncService> logger, InitialSyncService initialSyncService)
        {
            _appServicesDb = mongoClient.GetDatabase("AppServices");
            _mongoClient = mongoClient;
            _logger = logger;
            _initialSyncService = initialSyncService;
        }

        public async Task<IEnumerable<AppSyncMapping>> GetAppSyncMappings()
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");
            return await appCollection.Find<AppSyncMapping>(a => true).ToListAsync();
        }

        public async Task<AppSyncMapping?> SaveAppSyncMapping(AppSyncMapping appSyncMapping)
        {

            await _appServicesDb.CreateCollectionAsync("SyncMappings");

            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");

            var hasVersionChanged = false;

            // Have our mappings been modified? 
            var existingMapping = await appCollection.Find(a => a.Id == appSyncMapping.Id).FirstOrDefaultAsync();
                      
            foreach (var item in appSyncMapping.Collections)
            {
                var existingCollection = existingMapping?.Collections.FirstOrDefault(c => c.CollectionName == item.CollectionName && c.DatabaseName == item.DatabaseName);

                // Is this a new collection - if so then set the version to 1
                if (existingCollection is null)
                {
                    hasVersionChanged = true;
                    item.Version = 1;
                    continue;
                }

                IEnumerable<string> inFirstOnly = existingCollection.Fields!.Except(item.Fields!);
                IEnumerable<string> inSecondOnly = item.Fields!.Except(existingCollection.Fields!);

                var firstListCount = inFirstOnly.Count();
                var secondListCount = inSecondOnly.Count();

                var listsDiffer = firstListCount > 0 || secondListCount > 0;

                // Existing collection where the fields have changed so increment the version
                if (listsDiffer)
                {
                    hasVersionChanged = true;
                    item.Version += 1;
                }

            }

            if(hasVersionChanged)
            {
                appSyncMapping.Version += 1;
                appSyncMapping.HasInitialSyncComplete = false;
            }

            await appCollection.ReplaceOneAsync(
                record => record.Id == appSyncMapping.Id,
                appSyncMapping,
                new ReplaceOptions { IsUpsert = true }
            );

            if (!hasVersionChanged)
            {
                return null;                
            }

            return appSyncMapping;            
        }

        public async Task DeleteAppSyncMapping(string appId)
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");

            await appCollection.DeleteOneAsync(record => record.AppId == appId);
        }

        public async Task<AppSyncMapping?> GetAppInformation(string appName)
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");
            var appMapping = await appCollection.Find(a => a.AppName == appName).FirstOrDefaultAsync();

            if (appMapping is null)
            {
                return null;
            }

            if (!appMapping.HasInitialSyncComplete)
            {
                return null;
            }

            //appMapping.LastChecked = DateTime.Now;
            return appMapping;
        }

        public async Task<Dictionary<string, string>?> WriteDataToMongo(string appName, WebLocalCacheDataChange webLocalCacheDataChange)
        {
            if (!webLocalCacheDataChange.IsDeletion && webLocalCacheDataChange.Document is null) return new () {
                { "error", "Document is null" }
            };

            var appMapping = await GetAppInformation(appName);
            if (appMapping is null || !appMapping.HasInitialSyncComplete || appMapping.Collections is null) return new()
            {
                { "error", "App mapping not found or initial sync not complete"   }
            };

            var collectionMapping = appMapping.Collections
                .FirstOrDefault(c => $"{c.DatabaseName}_{c.CollectionName}".Replace("-", "_") == webLocalCacheDataChange.CollectionName);

            if (collectionMapping is null) return new()
            {
                { "error", "Collection mapping not found"   }
            };

            var database = _mongoClient.GetDatabase(collectionMapping.DatabaseName);
            var collection = database.GetCollection<BsonDocument>(collectionMapping.CollectionName);

            // Handle Deletion
            if (webLocalCacheDataChange.IsDeletion)
            {                
                await collection.DeleteOneAsync(record => record["_id"] == new ObjectId(webLocalCacheDataChange.Id));
                return new()
                {
                    { "message", $"Successfully deleted record." }
                };
            }

            // Filter allowed fields for insert/update
            var allowedFields = collectionMapping.Fields ?? new List<string>();
            var filteredDocument = new BsonDocument(
                webLocalCacheDataChange.Document!
                    .Where(field => allowedFields.Contains(field.Name)) // Only include allowed fields
                    .ToDictionary(kvp => kvp.Name, kvp => kvp.Value)
            );

            var filterById = Builders<BsonDocument>.Filter.Eq("_id", new ObjectId(webLocalCacheDataChange.Id));
            var existingRecord = await collection.Find(filterById).FirstOrDefaultAsync();

            if (existingRecord is null)
            {
                // Insert new record with filtered fields
                await collection.InsertOneAsync(filteredDocument);
                return new()
                {
                    { "message", $"Successfully added record." }
                };
            }
            
            // Prepare an update definition with only changed fields
                var updateDefinitions = new List<UpdateDefinition<BsonDocument>>();
                foreach (var field in allowedFields)
                {
                    if (filteredDocument.Contains(field) && (!existingRecord.Contains(field) || existingRecord[field] != filteredDocument[field]))
                    {
                        updateDefinitions.Add(Builders<BsonDocument>.Update.Set(field, filteredDocument[field]));
                    }
                }

                if (updateDefinitions.Any())
                {
                    var updateDefinition = Builders<BsonDocument>.Update.Combine(updateDefinitions);
                    await collection.UpdateOneAsync(filterById, updateDefinition);

                return new()
                {
                    { "message", $"Successfully updated record." }
                };
            }

            return new()
                {
                    { "message", $"No fields to update." }
                };

        }


        public bool UserHasPermission(string appId, string userId)
        {
            // Implement logic to check if the user has permission to access this app
            return true; // Assume they have permission for now
        }

        private IMongoCollection<BsonDocument> GetCollection(string appName, string databaseName, string collectionName)
        {
            var fullCollectionName = $"{appName}_{collectionName}";
            var db = _appServicesDb.Client.GetDatabase("AppServices");
            return db.GetCollection<BsonDocument>(fullCollectionName);
        }

        public async Task<List<WebSyncCollectionUpdateStatus>> CheckForCollectionUpdatesAsync(
    string appName,
    string userId,
    List<WebSyncCollectionInfo> localState)
        {
            var maxConcurrency = 10;
            using var throttler = new SemaphoreSlim(maxConcurrency);

            // Get all configured collections for this app (from syncmappings)
            var allMappings = await GetAppSyncMappings();
            var serverMappings = allMappings.FirstOrDefault(record => record.AppName == appName);
            
            var result = new ConcurrentBag<WebSyncCollectionUpdateStatus>();

            var localMap = localState.ToDictionary(
                x => $"{x.DatabaseName}.{x.CollectionName}",
                x => x,
                StringComparer.OrdinalIgnoreCase);

            var tasks = serverMappings.Collections.Select(async serverCollection =>
            {
                var key = $"{serverCollection.DatabaseName}.{serverCollection.CollectionName}";

                await throttler.WaitAsync();
                try
                {
                    var forceResync = false;
                    var recordsToDownload = 0;

                    if (localMap.TryGetValue(key, out var localInfo))
                    {
                        // Version mismatch?
                        if (localInfo.CollectionVersion != serverCollection.Version)
                        {
                            forceResync = true;
                        }

                        // Count only if version is fine
                        if (!forceResync)
                        {
                            var collection = GetCollection(appName, serverCollection.DatabaseName, serverCollection.CollectionName);
                            var filter = Builders<BsonDocument>.Filter.Gt("__meta.dateUpdated", localInfo.LastSyncDate);
                            recordsToDownload = (int)await collection.CountDocumentsAsync(filter);
                        }
                    }
                    else
                    {
                        // New collection not present on device — full count
                        var collection = GetCollection(appName, serverCollection.DatabaseName, serverCollection.CollectionName);
                        var count = (int)await collection.CountDocumentsAsync(new BsonDocument());
                        recordsToDownload = count;
                        forceResync = true; // new to device
                    }

                    result.Add(new WebSyncCollectionUpdateStatus
                    {
                        DatabaseName = serverCollection.DatabaseName,
                        CollectionName = serverCollection.CollectionName,
                        RecordsToDownload = recordsToDownload,
                        ForceFullResync = forceResync,
                        ShouldRemoveLocally = false
                    });
                }
                finally
                {
                    throttler.Release();
                }
            });

            // Now handle client collections that are no longer in server config
            var validKeys = new HashSet<string>(
                serverMappings.Collections.Select(c => $"{c.DatabaseName}.{c.CollectionName}"),
                StringComparer.OrdinalIgnoreCase);

            var removed = localState
                .Where(local => !validKeys.Contains($"{local.DatabaseName}.{local.CollectionName}"))
                .Select(local => new WebSyncCollectionUpdateStatus
                {
                    DatabaseName = local.DatabaseName,
                    CollectionName = local.CollectionName,
                    RecordsToDownload = 0,
                    //ShouldRemoveLocally = true,
                    //ForceFullResync = false
                });

            await Task.WhenAll(tasks);
            foreach (var r in removed)
            {
                result.Add(r);
            }

            return result.ToList();
        }



        public async Task<SyncResult> SyncAppDataAsync(
    string appName,
    string userId,
    string databaseName,
    string collectionName,
    bool isInitialSync = false,
    int pageNumber = 1,
    string? lastSyncedId = null,
    DateTime? lastSyncDate = null)
        {
            var appCollection = _appServicesDb.GetCollection<AppSyncMapping>("SyncMappings");
            var appMapping = await appCollection.Find(a => a.AppName == appName).FirstOrDefaultAsync();

            if (appMapping == null)
            {
                return new SyncResult { Success = false, ErrorMessage = "App mapping not found." };
            }

            // Check if the collection exists in app mapping
            var collectionMapping = appMapping.Collections.FirstOrDefault(c => c.CollectionName == collectionName);
            if (collectionMapping == null)
            {
                return new SyncResult { Success = false, ErrorMessage = "Collection mapping not found." };
            }

            var fullCollectionName = $"{appMapping.AppId}_{collectionName}";
            var sourceDb = _appServicesDb.Client.GetDatabase("AppServices");
            var sourceCollection = sourceDb.GetCollection<BsonDocument>(fullCollectionName);

            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = Builders<BsonDocument>.Filter.Empty;

            if (!isInitialSync)
            {
                if (lastSyncDate.HasValue)
                {
                    filter = filterBuilder.Gt("__meta.dateUpdated", lastSyncDate.Value);
                }
                else if (!string.IsNullOrEmpty(lastSyncedId))
                {
                    var idFilter = filterBuilder.Gt("_id", new ObjectId(lastSyncedId));
                    filter = idFilter;
                }
            }

            // Fetch batch of documents
            var documents = await FetchBatchAsync(sourceCollection, filter, pageNumber);

            return new SyncResult
            {
                Success = true,
                Data = documents,
                PageNumber = pageNumber,  // Increment with each call if looping through batches
                Count = documents.Count,
                AppName = appName,
                LastSyncedId = documents.LastOrDefault()  // Track last synced ID to resume on disconnect
            };
        }

        private async Task<List<string>> FetchBatchAsync(
    IMongoCollection<BsonDocument> collection,
    FilterDefinition<BsonDocument> filter,
    int pageNumber)
        {
            var sortBuilder = Builders<BsonDocument>.Sort;
            var sort = sortBuilder.Ascending("_id");

            var documents = await collection
                .Find(filter)
                .Sort(sort)
                .Skip((pageNumber - 1) * BatchSize)
                .Limit(BatchSize)
                .ToListAsync();

            return documents.Select(doc => doc.ToJson()).ToList();
        }


    }
}
