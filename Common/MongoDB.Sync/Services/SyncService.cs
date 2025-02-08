using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LiteDB;
using Microsoft.AspNetCore.SignalR.Client;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.Messages;
using MongoDB.Sync.Models;
using Services;
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

        private Func<HttpRequestMessage, Task>? _statusCheckAction;

        private Func<HttpRequestMessage, Task>? _preRequestAction;

        public readonly LocalDatabaseService _localDatabaseService;

        public event EventHandler<UpdatedData>? OnDataUpdated;

        public SyncService(LocalDatabaseService localDatabaseService, HttpService httpService, IMessenger messenger, string apiUrl, string appName)
        {
            _apiUrl = apiUrl;
            _appName = appName;
            _httpService = httpService;
            _messenger = messenger;
            _localDatabaseService = localDatabaseService;
        }


        private void HandleRealtimeUpdate(string jsonData)
        {
            var data = JsonSerializer.Deserialize<UpdatedData>(jsonData);
            if (data != null)
            {
                
                _messenger.Send<RealtimeUpdateReceivedMessage>(new RealtimeUpdateReceivedMessage(data));

                OnDataUpdated?.Invoke(this, data);
            }
        }

        private bool _syncIsStarting = false;

        private bool _syncHasStarted = false;

        private AppSyncMapping? _appDetails;

        private async Task PerformAPISync()
        {

            _appDetails = _localDatabaseService.GetAppMapping();

            // Loop through each collection stored
            foreach (var item in _appDetails!.Collections) {

                var dataSyncResult = (null as SyncResult);
                var pageNumber = 1;
                
                while (dataSyncResult == null || (dataSyncResult != null && dataSyncResult.Data!.Count > 0))
                {
                    Console.WriteLine($"Syncing {item.DatabaseName}.{item.CollectionName} Page {pageNumber}");

                    var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/sync"), HttpMethod.Post);
                    var formContent = new Dictionary<string, string>()
                    {
                        { "AppName", _appName },
                        { "DatabaseName", item.DatabaseName },
                        { "CollectionName", item.CollectionName },
                        { "PageNumber", pageNumber.ToString() }
                    };

                    

                    if (_appDetails.InitialSyncComplete)
                    {                        
                        var lastSyncDate = _localDatabaseService.GetLastSyncDateTime(item.DatabaseName, item.CollectionName);
                        Console.WriteLine($"Checking last sync date time for database: {item.DatabaseName} collection: {item.CollectionName}. Last date is: {lastSyncDate}");
                        if (lastSyncDate != null) formContent.Add("LastSyncDate", $"{lastSyncDate?.ToString("O")}");
                    } else
                    {
                        var lastSyncedId = _localDatabaseService.GetLastId(item.DatabaseName, item.CollectionName);
                        if (lastSyncedId != null) formContent.Add("LastSyncedId", lastSyncedId!.ToString());
                    }

                    builder.WithFormContent(formContent);

                    if(_preRequestAction != null) builder.PreRequest(_preRequestAction);

                    if(_statusCheckAction != null) builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusCheckAction);

                    var response = await builder                        
                        .WithRetry(3)
                        .SendAsync<SyncResult>();

                    if (response is null || !response.Success) break;

                    dataSyncResult = response.Result;

                    foreach (var rawDoc in response.Result!.Data)
                    {
                        _messenger.Send<RealtimeUpdateReceivedMessage>(new RealtimeUpdateReceivedMessage(new UpdatedData()
                        {
                            Database = item.DatabaseName,
                            CollectionName = item.CollectionName,
                            Document = rawDoc
                        }));
                    }

                    pageNumber++;

                }

            }

            _localDatabaseService.InitialSyncComplete();

        }

        public async Task StartSyncAsync(Func<HttpRequestMessage, Task>? preRequestAction, Func<HttpRequestMessage, Task>? statusChangeAction)
        {

            if (_syncHasStarted || _syncIsStarting) return;

            if(statusChangeAction != null) _statusCheckAction = statusChangeAction;
            if (preRequestAction != null) _preRequestAction = preRequestAction;

            _syncIsStarting = true;

            // Get all of the collections from the API
            _appDetails = await GetAppInformation();

            if(_appDetails is null) throw new NullReferenceException(nameof(_appDetails));

            _appDetails.AppName = _appName;

            var hubUrl = $"{_apiUrl}/hubs/update";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .Build();

            // Set up handler for receiving real-time updates
            _hubConnection.On<string>("ReceiveUpdate", HandleRealtimeUpdate);

            if (_hubConnection.State == HubConnectionState.Disconnected)
            {
                await _hubConnection.StartAsync();
            }

            await _hubConnection.InvokeAsync("SubscribeToApp", _appName);

            _messenger.Send<InitializeLocalDataMappingMessage>(new InitializeLocalDataMappingMessage(_appDetails));

            await PerformAPISync();
            
            _syncIsStarting = false;

            return;
        }

        public async Task StopSyncAsync()
        {
            if (!_syncHasStarted) return;

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection!.StopAsync();
            }

        }

        public async Task ResumeSyncAsync()
        {
            await StartSyncAsync(null, _statusCheckAction);
        }

        public async Task ClearCacheAsync()
        {
            await StopSyncAsync();

            _messenger.Send<ClearLocalCacheMessage>(new ClearLocalCacheMessage(false));

            
        }

        #region "API Checks"

        /// <summary>
        /// Collect the information about all collections in our app database
        /// </summary>
        /// <returns></returns>
        public async Task<AppSyncMapping?> GetAppInformation()
        {

            var builder = _httpService.CreateBuilder(new Uri($"{_apiUrl}/api/DataSync/Collect"), HttpMethod.Post);
            if (_preRequestAction != null) builder.PreRequest(_preRequestAction);

            if (_statusCheckAction != null) builder.OnStatus(System.Net.HttpStatusCode.Unauthorized, _statusCheckAction);

            var response = await builder
                .WithFormContent(new()
                {
                    { "AppName", _appName }
                })                
                .WithRetry(3)
                .SendAsync<AppSyncMapping>();

            if (response is null) throw new NullReferenceException("The request failed and returned no response");

            return response.Result;

        }

        #endregion


    }
}
