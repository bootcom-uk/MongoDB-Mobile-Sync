using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Sync.Web.Interfaces;
using MongoDB.Sync.Web.Models.SyncModels;

namespace MongoDB.Sync.Web.Services
{
    public class AppSyncService : IAppSyncService
    {
        private readonly IMongoDatabase _appServicesDb;
        private readonly ILogger<AppSyncService> _logger;
        private const int BatchSize = 100; // Define your batch size

        public AppSyncService(IMongoClient mongoClient, ILogger<AppSyncService> logger)
        {
            _appServicesDb = mongoClient.GetDatabase("AppServices");
            _logger = logger;
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

            return appMapping;
        }

        public bool UserHasPermission(string appId, string userId)
        {
            // Implement logic to check if the user has permission to access this app
            return true; // Assume they have permission for now
        }

        public async Task<SyncResult> SyncAppDataAsync(
    string appName,
    string userId,
    DateTime? lastSyncDate,
    string databaseName,
    string collectionName,
    int pageNumber = 1,
    string? lastSyncedId = null)
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
            var filter = lastSyncDate.HasValue
                ? filterBuilder.Gt("__meta.dateUpdated", lastSyncDate.Value)
                : filterBuilder.Empty;

            if (!string.IsNullOrEmpty(lastSyncedId))
            {
                var idFilter = filterBuilder.Gt("_id", new ObjectId(lastSyncedId));
                filter &= idFilter;
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
            var documents = await collection
                .Find(filter)
                .Limit(BatchSize)
                .Skip((pageNumber - 1) * BatchSize)
                .ToListAsync();

            // Convert each document to JSON string to avoid BsonDocument serialization issues
            return documents.Select(doc => doc.ToJson()).ToList();
        }
    }
}
