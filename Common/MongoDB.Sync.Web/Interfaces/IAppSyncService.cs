using MongoDB.Sync.Web.Models.SyncModels;

namespace MongoDB.Sync.Web.Interfaces
{
    public interface IAppSyncService
    {
        bool UserHasPermission(string appId, string userId);

        Task<SyncResult> SyncAppDataAsync(string appName,
    string userId,
    DateTime? lastSyncDate,
    string databaseName,
    string collectionName,
    int pageNumber = 1,
    string? lastSyncedId = null);

        Task<AppSyncMapping?> GetAppInformation(string appName);
    }
}
