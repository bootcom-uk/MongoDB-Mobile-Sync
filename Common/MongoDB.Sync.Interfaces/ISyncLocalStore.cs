using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Interfaces
{
    public interface ISyncApiClient
    {
        void Configure(string baseUrl);

        Task<PagedSyncResult> GetCollectionPageAsync( string appName, string collectionName, DateTime? lastUpdated, int page, int pageSize);

        Task ConnectRealtimeAsync(Func<RealtimeUpdate, Task> onUpdate);
    }
}
