using MongoDB.Sync.Client.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Core
{
    public sealed class SyncStateManager
    {
        private readonly ISyncLocalStore _localStore;

        public SyncStateManager(ISyncLocalStore localStore)
        {
            _localStore = localStore;
        }

        public Task<bool> HasInitialSyncCompletedAsync()
            => _localStore.HasInitialSyncCompletedAsync();

        public Task MarkInitialSyncCompleteAsync()
            => _localStore.MarkInitialSyncCompleteAsync();

        public Task<DateTime?> GetLastUpdatedAsync(string collectionName)
            => _localStore.GetLastUpdatedAsync(collectionName);

        public Task SetLastUpdatedAsync(string collectionName, DateTime timestamp)
            => _localStore.SetLastUpdatedAsync(collectionName, timestamp);
    }
}
