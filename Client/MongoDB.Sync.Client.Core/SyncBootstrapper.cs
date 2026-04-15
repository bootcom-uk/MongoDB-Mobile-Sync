using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.Client.Core
{
    public sealed class SyncBootstrapper
    {
        private readonly SyncEngine _syncEngine;
        private readonly SyncRegistry _registry;
        private readonly SyncStateManager _stateManager;

        public SyncBootstrapper( SyncEngine syncEngine, SyncRegistry registry, SyncStateManager stateManager)
        {
            _syncEngine = syncEngine;
            _registry = registry;
            _stateManager = stateManager;
        }

        public async Task InitializeAsync( string appName, string apiBaseUrl, string localDbPath, bool enableSignalR, Action<CollectionConfigurator> configureCollections)
        {
            // 1. Setup registry
            var configurator = new CollectionConfigurator(_registry);
            configureCollections?.Invoke(configurator);

            // 2. Configure engine
            _syncEngine.Configure(appName, apiBaseUrl, localDbPath);

            // 3. Initial sync logic
            if (!_stateManager.HasInitialSyncCompleted())
            {
                await _syncEngine.PerformInitialSyncAsync();
                _stateManager.MarkInitialSyncComplete();
            }
            else
            {
                await _syncEngine.ResumeSyncAsync();
            }

            // 4. SignalR
            if (enableSignalR)
            {
                await _syncEngine.StartRealtimeAsync();
            }
        }
    }
}
