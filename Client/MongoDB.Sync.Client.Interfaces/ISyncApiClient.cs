using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface ISyncApiClient
    {
        void Configure(string baseUrl);

        Task<IPagedSyncResult> GetCollectionPageAsync(string appName, string databaseName, string collectionName, bool initialSync, int pageNumber, DateTime? lastSyncDate, string? lastSyncedId);

        Task ConnectRealtimeAsync(Func<IRealtimeUpdate, Task> onUpdate);
    }
}
