using MongoDB.Sync.Client.Interfaces;
using MongoDB.Sync.Client.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Core
{
    public sealed class SyncEngine
    {
        private readonly ISyncApiClient _apiClient;
        private readonly ISyncLocalStore _localStore;
        private readonly SyncRegistry _registry;
        private readonly SyncStateManager _stateManager;

        private string? _appName;
        private string? _apiBaseUrl;

        public SyncEngine( ISyncApiClient apiClient, ISyncLocalStore localStore, SyncRegistry registry, SyncStateManager stateManager)
        {
            _apiClient = apiClient;
            _localStore = localStore;
            _registry = registry;
            _stateManager = stateManager;
        }

        public void Configure(string appName, string apiBaseUrl)
        {
            _appName = appName;
            _apiBaseUrl = apiBaseUrl;

            _apiClient.Configure(apiBaseUrl);
        }

        public async Task PerformInitialSyncAsync()
        {
            foreach (var collection in _registry.GetAll())
            {
                await SyncCollectionAsync(collection, fullSync: true);
            }
        }

        public async Task ResumeSyncAsync()
        {
            foreach (var collection in _registry.GetAll())
            {                
                await SyncCollectionAsync(collection, fullSync: false);
            }
        }

        private async Task SyncCollectionAsync(SyncCollectionDefinition collection, bool fullSync)
        {
            DateTime? lastUpdated = null;

            if (!fullSync)
            {
                lastUpdated = await _stateManager
                    .GetLastUpdatedAsync(collection.CollectionName);
            }

            int page = 1;
            const int pageSize = 500;

            //while (true)
            //{
            //    var result = await _apiClient.GetCollectionPageAsync(
            //        _appName!,
            //        collection.DatabaseName,
            //        collection.CollectionName,                    
            //        lastUpdated,
            //        page,
            //        pageSize);

            //    if (result.Items.Count == 0)
            //        break;

            //    await _localStore.UpsertAsync(
            //        collection.CollectionName,
            //        result.Items);

            //    if (result.MaxTimestamp != null)
            //    {
            //        await _stateManager.SetLastUpdatedAsync(
            //            collection.CollectionName,
            //            result.MaxTimestamp.Value);
            //    }

            //    page++;
            //}
        }

        public async Task StartRealtimeAsync()
        {
            await _apiClient.ConnectRealtimeAsync(async update =>
            {
                var definition = _registry.Get(update.CollectionName);

                if (definition == null)
                    return;

                await _localStore.UpsertAsync(
                    update.CollectionName,
                    new[] { update.Entity });

                if (update.Timestamp != null)
                {
                    await _stateManager.SetLastUpdatedAsync(
                        update.CollectionName,
                        update.Timestamp.Value);
                }
            });
        }
    }
}
