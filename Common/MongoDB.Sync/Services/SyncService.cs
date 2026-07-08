using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using Microsoft.AspNetCore.SignalR.Client;
using MongoDB.Sync.Converters;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using MongoDB.Sync.Models.Web;
using Services;
using System.Net;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MongoDB.Sync.Services
{
    public partial class SyncService : ObservableObject, ISyncService
    {
        private readonly string _apiUrl;
        private readonly string _appName;
        private HubConnection? _hubConnection;
        private readonly HttpService _httpService;
        private readonly IMessenger _messenger;
        private readonly NetworkStateService _networkStateService;
        private Func<HttpRequestMessage, Task>? _statusCheckAction;
        private Func<HttpRequestMessage, Task>? _preRequestAction;
        public readonly LocalDatabaseSyncService _localDatabaseService;
        private readonly LocalCacheService _localCacheService;

        [ObservableProperty]
        bool syncIsStarting  = false;

        [ObservableProperty]
        bool syncHasCompleted = false;

        private AppSyncMapping? _appDetails;

        [ObservableProperty]    
        bool appSyncInProgress = false;

        private JsonSerializerOptions _serializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ObjectIdConverter() }
        };

        public SyncService(LocalDatabaseSyncService localDatabaseService, HttpService httpService, IMessenger messenger, NetworkStateService networkStateService, string apiUrl, string appName, Func<HttpRequestMessage, Task>? preRequestAction, Func<HttpRequestMessage, Task>? statusChangeAction, LocalCacheService localCacheService)
        {
            _apiUrl = apiUrl;
            _appName = appName;
            _httpService = httpService;
            _messenger = messenger;
            _localDatabaseService = localDatabaseService;
            _networkStateService = networkStateService;
            _statusCheckAction = statusChangeAction;
            _preRequestAction = preRequestAction;
            _localCacheService = localCacheService;

            _messenger.Register<NetworkStateChangedMessage>(this, async (r, m) =>
            {
                // If we're not connected, don't do anything
                if (m.Value != NetworkStateService.NetworkState.Connected) return;

                // When we are connected, resubscribe to SignalR and perform a sync
                await ResubscribeToSignalR();
            });
        }

        /// <summary>
        /// Starts the synchronization process. This method checks if synchronization has already completed or is currently starting. If not, it verifies the network connection state. If connected, it performs an API synchronization to ensure local data is up-to-date with the server, and then establishes a SignalR connection for real-time updates. The method sets flags to indicate the synchronization state and updates the local cache service accordingly.
        /// </summary>
        /// <returns></returns>
        public async Task StartSyncAsync()
        {
            if (SyncHasCompleted || SyncIsStarting) return;
            
            if(_networkStateService.CurrentState != NetworkStateService.NetworkState.Connected)
            {
                SyncIsStarting = true;
                SyncIsStarting = false;
                SyncHasCompleted = true;
                _localCacheService.SyncHasCompleted = true;
                return;
            }

            SyncIsStarting = true;

            await PerformAPISync();
            await StartSignalRAsync();

            SyncIsStarting = false;
            SyncHasCompleted = true;

            _localCacheService.SyncHasCompleted = true;
        }

        /// <summary>
        /// Stops the synchronization process if it has completed. This method checks if the synchronization has completed and if the SignalR hub connection is active. If both conditions are met, it stops the SignalR connection, effectively halting any real-time updates and synchronization activities. This is useful for scenarios where you want to temporarily stop receiving updates or when shutting down the application.
        /// </summary>
        /// <returns></returns>
        public async Task StopSyncAsync()
        {
            if (!SyncHasCompleted) return;

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.StopAsync();
            }
        }

        public async Task ResumeSyncAsync() => await StartSyncAsync();

        /// <summary>
        /// Clears the local cache and stops any ongoing synchronization processes. This method first stops the synchronization process if it is currently running, and then sends a message to clear the local cache. It is useful for resetting the local state of the application, especially when there are issues with data consistency or when a fresh start is needed.
        /// </summary>
        /// <returns></returns>
        public async Task ClearCacheAsync()
        {
            await StopSyncAsync();
            _messenger.Send(new ClearLocalCacheMessage(false));
        }

        private async Task PerformAPISync()
        {

            // Step 1: Get server-side app mapping
            var serverAppInfo = await GetAppInformation();
            if (serverAppInfo is null)
                throw new Exception("Failed to get remote app mapping.");

            _messenger.Send<APISyncStartedMessage>(new APISyncStartedMessage(true));

            _messenger.Send(new InitializeLocalDataMappingMessage(serverAppInfo));

            // Step 2: Get local app mapping
            _appDetails = _localDatabaseService.GetAppMapping();
            if (_appDetails is null)
                throw new NullReferenceException(nameof(_appDetails));

            // Step 3: Build local collection state with LastSyncDate
            var localCollectionInfos = _appDetails.Collections.Select(c =>
            {
                var latestUpdate = _localDatabaseService.GetLastSyncDateTime(c.DatabaseName, c.CollectionName);

                return new WebSyncCollectionInfo
                {
                    DatabaseName = c.DatabaseName,
                    CollectionName = c.CollectionName,
                    CollectionVersion = c.Version,
                    LastSyncDate = latestUpdate ?? DateTime.MinValue
                };
            }).ToList();

            // Step 4: Build the request object
            var updateRequest = new WebSyncCollectionCheckRequest
            {
                AppName = _appDetails.AppName,
                Collections = localCollectionInfos
            };

            // Step 5: Call check-updates API using _httpService
            var checkUpdatesUri = new Uri($"{_apiUrl}/api/DataSync/check-updates");
            var builder = _httpService.CreateBuilder(checkUpdatesUri, HttpMethod.Post)
                .WithJsonContent(updateRequest);

            if (_preRequestAction != null) builder.PreRequest(_preRequestAction);
            if (_statusCheckAction != null) builder.OnStatus(HttpStatusCode.Unauthorized, _statusCheckAction);

            var updateStatuses = await builder
                .WithRetry(3, TimeSpan.FromSeconds(2))
                .SendAsync<WebSyncCollectionCheckResponse>(_serializationOptions);

            if (updateStatuses == null)
                throw new Exception("Failed to retrieve update statuses from server.");

            if(!updateStatuses.Success)
                throw new Exception(updateStatuses.Exception);

            // Step 6: Process each collection in parallel
            int maxConcurrency = 5;
            using var throttler = new SemaphoreSlim(maxConcurrency);

            var syncTasks = updateStatuses.Result!.Updates.Select(async update =>
            {
                await throttler.WaitAsync();
                try
                {
                    var collectionKey = $"{update.DatabaseName}_{update.CollectionName}".Replace("-", "_");

                    if (update.ShouldRemoveLocally)
                    {
                        _localDatabaseService.ClearCollection(collectionKey);
                        return;
                    }

                    if (update.ForceFullResync)
                    {
                        _localDatabaseService.ClearCollection(collectionKey);
                    }

                    if (update.RecordsToDownload > 0 || update.ForceFullResync)
                    {
                        var serverCollection = serverAppInfo.Collections.FirstOrDefault(c =>
                            c.DatabaseName == update.DatabaseName &&
                            c.CollectionName == update.CollectionName);

                        if (serverCollection != null)
                        {
                            await ProcessCollectionUpdate(serverCollection);
                        }
                    }
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(syncTasks);

            _messenger.Send<APISyncCompletedMessage>(new APISyncCompletedMessage(true));

            // Step 7: Mark initial sync as complete
            _localDatabaseService.InitialSyncComplete();
        }

        public async Task ProcessCollectionUpdate(CollectionMapping item)
        {
            SyncResult? dataSyncResult = null;
            int pageNumber = 1;
            var collectionName = $"{item.DatabaseName}_{item.CollectionName}".Replace("-", "_");
            var lastId = _localDatabaseService.GetLastId(item.DatabaseName, item.CollectionName);
            var initialSync = lastId == null;

            while (dataSyncResult == null || dataSyncResult.Data?.Count > 0)
            {
                var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/sync"), HttpMethod.Post);
                var formContent = new Dictionary<string, string>
                {
                    { "AppName", _appName },
                    { "DatabaseName", item.DatabaseName },
                    { "CollectionName", item.CollectionName },
                    { "PageNumber", pageNumber.ToString() },
                    { "InitialSync", initialSync.ToString() }
                };

                var lastSyncDate = _localDatabaseService.GetLastSyncDateTime(item.DatabaseName, item.CollectionName);
                if (lastSyncDate != null) formContent.Add("LastSyncDate", lastSyncDate?.ToString("O"));

                var lastSyncedId = _localDatabaseService.GetLastId(item.DatabaseName, item.CollectionName);
                if (lastSyncedId != null) formContent.Add("LastSyncedId", lastSyncedId.ToString());

                builder.WithFormContent(formContent);

                if (_preRequestAction != null) builder.PreRequest(_preRequestAction);
                if (_statusCheckAction != null) builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusCheckAction);

                var response = await builder.WithRetry(3, TimeSpan.FromSeconds(2)).SendAsync<SyncResult>(_serializationOptions);

                if (response == null || !response.Success) break;
                dataSyncResult = response.Result;

                foreach (var rawDoc in response.Result!.Data!)
                {
                    var updatedData = new UpdatedData
                    {
                        Database = item.DatabaseName,
                        CollectionName = item.CollectionName,
                        Document = rawDoc
                    };

                    _messenger.Send(new APISyncMessageReceived(JsonSerializer.Serialize(updatedData)));
                }

                _messenger.Send(new APISyncProcessingMessage(new APISyncProcessingDetail()
                {
                    
                    DatabaseName = item.DatabaseName,
                    CollectionName = item.CollectionName,
                    PageNumber = pageNumber,
                    RecordsProcessed = response.Result.Data.Count
                }));

                pageNumber++;
            }
        }

        private async Task StartSignalRAsync()
        {
            _networkStateService.ChangeNetworkState(NetworkStateService.NetworkState.Connected);

            if (_hubConnection == null)
            {
                _hubConnection = new HubConnectionBuilder()
                    .WithUrl($"{_apiUrl}/hubs/update")
                    .WithAutomaticReconnect(new[]
                    {
                        TimeSpan.FromSeconds(0),
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(10)
                    })
                    .Build();
            }

            _hubConnection.Reconnecting += (error) =>
            {
                Console.WriteLine("🔄 SignalR reconnecting...");
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += async (connectionId) =>
            {
                Console.WriteLine($"✅ SignalR reconnected! ConnectionId: {connectionId}");
                _networkStateService.ChangeNetworkState(NetworkStateService.NetworkState.Connected);
                await ResubscribeToSignalR();
            };

            _hubConnection.Closed += async (error) =>
            {
                Console.WriteLine("❌ SignalR connection closed. Attempting reconnect...");
                _networkStateService.ChangeNetworkState(NetworkStateService.NetworkState.Disconnected);
                await AttemptReconnect();
            };

            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("SubscribeToApp", _appName);

            _hubConnection.On<string>("ReceiveUpdate", HandleRealtimeUpdate);
            _hubConnection.On("AppSyncStarted", () => AppSyncInProgress = true);
            _hubConnection.On<string>("AppSyncComplete", HandleAppSchemaUpdate);
        }

        private async Task AttemptReconnect()
        {
            while (_hubConnection!.State == HubConnectionState.Disconnected)
            {
                try
                {
                    Console.WriteLine("🔄 Trying to reconnect to SignalR...");
                    await _hubConnection.StartAsync();
                    Console.WriteLine("✅ Reconnected!");
                    await ResubscribeToSignalR();
                    return;
                }
                catch
                {
                    Console.WriteLine("⚠️ Reconnect failed. Retrying in 5 seconds...");
                    await Task.Delay(5000);
                }
            }
        }

        private async Task ResubscribeToSignalR()
        {
            await _hubConnection!.InvokeAsync("SubscribeToApp", _appName);
            await PerformAPISync();
        }

        private void HandleRealtimeUpdate(string jsonData)
        {
            if (jsonData != null)
            {
                _messenger.Send(new RealtimeUpdateReceivedMessage(jsonData));
            }
        }

        private async Task HandleAppSchemaUpdate(string jsonSchema)
        {
            var newSchema = JsonSerializer.Deserialize<AppSyncMapping>(jsonSchema);
            if (newSchema == null || newSchema.Version == _appDetails?.Version) return;

            foreach (var collection in newSchema.Collections)
            {
                if (collection.Version != _appDetails!.Collections.FirstOrDefault(c => c.CollectionName == collection.CollectionName)?.Version)
                {
                    _localDatabaseService.ClearCollection($"{collection.DatabaseName}_{collection.CollectionName}".Replace("-", "_"));
                    await ProcessCollectionUpdate(collection);
                }
            }

            AppSyncInProgress = false;
        }

        public async Task<AppSyncMapping?> GetAppInformation()
        {
            var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/Collect"), HttpMethod.Post);

            if (_preRequestAction != null) builder.PreRequest(_preRequestAction);
            if (_statusCheckAction != null) builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusCheckAction);

            var response = await builder
                .WithFormContent(new Dictionary<string, string> { { "AppName", _appName } })
                .WithRetry(3, TimeSpan.FromSeconds(2))
                .SendAsync<AppSyncMapping>(_serializationOptions);

            if (response is null) throw new NullReferenceException("The request failed and returned no response");
            if (!response.Success && !response.HasInternetConnection) {
                _networkStateService.ChangeNetworkState(NetworkStateService.NetworkState.Disconnected);
                return null;
            }

            if (!response.Success) throw new Exception(response.Exception);

            var appSyncMapping = response.Result;

            var serverDateTimeHeader = response.Headers?.FirstOrDefault(h => string.Equals(h.Key, "ServerDateTime", StringComparison.InvariantCultureIgnoreCase)).Value;

            if (serverDateTimeHeader != null)
            {
                appSyncMapping!.ServerDateTime = DateTime.Parse(serverDateTimeHeader);
            }
            return appSyncMapping;
        }
    }
}
