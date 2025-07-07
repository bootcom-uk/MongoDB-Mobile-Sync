using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using Microsoft.AspNetCore.SignalR.Client;
using MongoDB.Sync.Converters;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using Services;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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

        private JsonSerializerOptions _serializationOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new ObjectIdConverter() }
        };

        public SyncService(LocalDatabaseSyncService localDatabaseService, HttpService httpService, IMessenger messenger, NetworkStateService networkStateService, string apiUrl, string appName, Func<HttpRequestMessage, Task>? preRequestAction, Func<HttpRequestMessage, Task>? statusChangeAction)
        {
            _apiUrl = apiUrl;
            _appName = appName;
            _httpService = httpService;
            _messenger = messenger;
            _localDatabaseService = localDatabaseService;
            _networkStateService = networkStateService;
            _statusCheckAction = statusChangeAction;
            _preRequestAction = preRequestAction;
        }

        private bool _syncIsStarting = false;
        private bool _syncHasStarted = false;
        private AppSyncMapping? _appDetails;
        public bool AppSyncInProgress { get; set; } = false;

        public async Task StartSyncAsync()
        {
            if (_syncHasStarted || _syncIsStarting) return;
            _syncIsStarting = true;

            _appDetails = await GetAppInformation();
            if (_appDetails is null) throw new NullReferenceException(nameof(_appDetails));

            _appDetails.AppName = _appName;
            _messenger.Send(new InitializeLocalDataMappingMessage(_appDetails));

            await PerformAPISync();
            await StartSignalRAsync();

            _syncIsStarting = false;
            _syncHasStarted = true;
        }

        public async Task StopSyncAsync()
        {
            if (!_syncHasStarted) return;

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
            _appDetails = _localDatabaseService.GetAppMapping();
            if (_appDetails is null) throw new NullReferenceException(nameof(_appDetails));

            var syncTasks = _appDetails.Collections
                .Select(ProcessCollectionUpdate); // starts all tasks

            await Task.WhenAll(syncTasks); // waits for all to complete

            _localDatabaseService.InitialSyncComplete();
        }

        //private async Task PerformAPISync()
        //{
        //    _appDetails = _localDatabaseService.GetAppMapping();
        //    if (_appDetails is null) throw new NullReferenceException(nameof(_appDetails));

        //    foreach (var item in _appDetails.Collections)
        //    {
        //        await ProcessCollectionUpdate(item);
        //    }

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

                var response = await builder.WithRetry(3).SendAsync<SyncResult>(_serializationOptions);

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
                .WithRetry(3)
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
