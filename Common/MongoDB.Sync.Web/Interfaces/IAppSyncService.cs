using MongoDB.Sync.Web.Models.SyncModels;

namespace MongoDB.Sync.Web.Interfaces
{
    public interface IAppSyncService
    {

        Task<IEnumerable<AppSyncMapping>> GetAppSyncMappings();

        Task<AppSyncMapping?> SaveAppSyncMapping(AppSyncMapping appSyncMapping);

        Task DeleteAppSyncMapping(string appId);

        bool UserHasPermission(string appId, string userId);

        Task<Dictionary<string, string>?> WriteDataToMongo(string appName, WebLocalCacheDataChange webLocalCacheDataChange);

        Task<SyncResult> SyncAppDataAsync(
    string appName,
    string userId,
    string databaseName,
    string collectionName,
    bool isInitialSync = false,
    int pageNumber = 1,
    string? lastSyncedId = null,
    DateTime? lastSyncDate = null);

        Task<AppSyncMapping?> GetAppInformation(string appName);
    }
}
