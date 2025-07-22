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
    public class SyncService : ObservableObject, ISyncService
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

        public bool SyncIsStarting { get; set; } = false;
        public bool SyncHasCompleted { get; set; } = false;

        private AppSyncMapping? _appDetails;
        public bool AppSyncInProgress { get; set; } = false;

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
        }



         public async Task StartSyncAsync()
        {
            if (SyncHasCompleted || SyncIsStarting) return;
            SyncIsStarting = true;

            await PerformAPISync();
            await StartSignalRAsync();

            SyncIsStarting = false;
            SyncHasCompleted = true;

            _localCacheService.SyncHasCompleted = true;
        }

        public async Task StopSyncAsync()
        {
            if (!SyncHasCompleted) return;

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.StopAsync();
            }
        }

        public async Task ResumeSyncAsync() => await StartSyncAsync();

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
                    LastSyncDate = latestUpdate
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

            // Step 7: Mark initial sync as complete
            _localDatabaseService.InitialSyncComplete();
        }



        //private async Task PerformAPISync()
        //{

        //    // Get details from server
        //    var serverAppInfo = await GetAppInformation();
        //    if (serverAppInfo is null)
        //        throw new Exception("Failed to get remote app mapping.");

        //    _messenger.Send(new InitializeLocalDataMappingMessage(serverAppInfo));

        //    // Get details locally
        //    _appDetails = _localDatabaseService.GetAppMapping();
        //    if (_appDetails is null)
        //        throw new NullReferenceException(nameof(_appDetails));


        //    var localCollectionKeys = _appDetails.Collections
        //        .Select(c => $"{c.DatabaseName}.{c.CollectionName}")
        //        .ToHashSet();

        //    var serverCollectionKeys = serverAppInfo.Collections
        //        .Select(c => $"{c.DatabaseName}.{c.CollectionName}")
        //        .ToHashSet();

        //    var collectionsToRemove = localCollectionKeys.Except(serverCollectionKeys);
        //    foreach (var key in collectionsToRemove)
        //    {
        //        var parts = key.Split('.');
        //        _localDatabaseService.ClearCollection($"{parts[0]}_{parts[1]}".Replace("-", "_"));
        //    }

        //    int maxConcurrency = 5;
        //    using var throttler = new SemaphoreSlim(maxConcurrency);

        //    var syncTasks = serverAppInfo.Collections.Select(async serverCollection =>
        //    {
        //        await throttler.WaitAsync();
        //        try
        //        {
        //            var localCollection = _appDetails.Collections.FirstOrDefault(c =>
        //                c.DatabaseName == serverCollection.DatabaseName &&
        //                c.CollectionName == serverCollection.CollectionName);

        //            if (localCollection == null || localCollection.Version != serverCollection.Version)
        //            {
        //                _localDatabaseService.ClearCollection($"{serverCollection.DatabaseName}_{serverCollection.CollectionName}".Replace("-", "_"));
        //            }

        //            await ProcessCollectionUpdate(serverCollection);
        //        }
        //        finally
        //        {
        //            throttler.Release();
        //        }
        //    });

        //    await Task.WhenAll(syncTasks);

        //    _localDatabaseService.InitialSyncComplete();
        //}


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
            if (!response.Success) throw new Exception(response.Exception);

            var appSyncMapping = response.Result;
            if (response.Headers?["ServerDateTime"] != null)
            {
                appSyncMapping!.ServerDateTime = DateTime.Parse(response.Headers["ServerDateTime"]);
            }
            return appSyncMapping;
        }
    }
}
