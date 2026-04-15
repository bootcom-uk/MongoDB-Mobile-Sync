using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Interfaces
{
    public interface ISyncLocalStore
    {
        Task InitializeAsync();

        Task UpsertAsync(string collectionName, IEnumerable<object> entities);

        Task<DateTime?> GetLastUpdatedAsync(string collectionName);

        Task SetLastUpdatedAsync(string collectionName, DateTime timestamp);

        Task<bool> HasInitialSyncCompletedAsync();

        Task MarkInitialSyncCompleteAsync();
    }
}
