using MongoDB.Sync.Web.Models.SyncModels;

namespace MongoDB.Sync.Web.Interfaces
{
    public interface IAppSyncService
    {
        bool UserHasPermission(string appId, string userId);

        Task<Dictionary<string, string>?> WriteDataToMongo(string appName, WebLocalCacheDataChange webLocalCacheDataChange);

        Task<SyncResult> SyncAppDataAsync(string appName,
    string userId,
    string databaseName,
    string collectionName,
    int pageNumber = 1,
    string? lastSyncedId = null,
    DateTime? lastSyncDate = null);

        Task<AppSyncMapping?> GetAppInformation(string appName);
    }
}
