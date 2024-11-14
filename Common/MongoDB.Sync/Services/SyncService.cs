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
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace MongoDB.Sync.Services
{

    public class SyncService : ObservableObject, ISyncService
    {
        private readonly string _apiUrl;
        private readonly string _appName;
        private HubConnection? _hubConnection;
        
        private readonly SyncHttpService _syncHttpService;

        private readonly IMessenger _messenger;

        public readonly LocalDatabaseService _localDatabaseService;

        public event EventHandler<UpdatedData>? OnDataUpdated;

        public event EventHandler<string>? OnAuthorizationRequested;

        public event EventHandler OnAuthorizationFailed;

        public SyncService(LocalDatabaseService localDatabaseService, SyncHttpService syncHttpService, IMessenger messenger, string apiUrl, string appName)
        {
            _apiUrl = apiUrl;
            _appName = appName;
            _syncHttpService = syncHttpService;
            _messenger = messenger;
            _localDatabaseService = localDatabaseService;
        }

        // Initial data sync from the API
        private async Task FetchDataAsync(DateTime? lastSyncDate, string? lastSyncedId = null)
        {

            var httpRequestMessage = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"{_apiUrl}/sync")
            };

            var formContent = new MultipartFormDataContent();
            formContent.Add(new StringContent(_appName, Encoding.UTF8, MediaTypeNames.Text.Plain), "appName");
            if(lastSyncDate != null) formContent.Add(new StringContent(lastSyncDate.Value.ToString(), Encoding.UTF8, MediaTypeNames.Text.Plain), "lastSyncDate");
            if(lastSyncedId != null) formContent.Add(new StringContent(lastSyncedId, Encoding.UTF8, MediaTypeNames.Text.Plain), "lastSyncedId");
            httpRequestMessage.Content = formContent;

            var response = await _syncHttpService.MakeRequest<SyncResult>(httpRequestMessage);
            if (response != null && response.Success)
            {

                foreach (var item in response.Data)
                {
                    _messenger.Send<RealtimeUpdateReceivedMessage>(new RealtimeUpdateReceivedMessage(new UpdatedData
                    {
                        CollectionName = response.CollectionName!,
                        Database = response.DatabaseName!,
                        Document = item,
                        Id = null,
                        UpdatedAt = null
                    }));
                }
                    
            }
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
            // Loop through each collection stored
            foreach (var item in _appDetails!.Collections) {

                var dataSyncResult = (null as SyncResult);
                var pageNumber = 1;
                var lastSyncedId = _localDatabaseService.GetLastId(item.DatabaseName, item.CollectionName);

                while (dataSyncResult == null || (dataSyncResult != null && dataSyncResult.Data!.Count > 0))
                {
                    var httpRequestMessage = new HttpRequestMessage()
                    {
                        Method = HttpMethod.Post,
                        RequestUri = new Uri($"{_apiUrl}/api/DataSync/sync")
                    };

                    var formContent = new MultipartFormDataContent();
                    formContent.Add(new StringContent(_appName, Encoding.UTF8, MediaTypeNames.Text.Plain), "AppName");
                    formContent.Add(new StringContent(item.DatabaseName, Encoding.UTF8, MediaTypeNames.Text.Plain), "DatabaseName");
                    formContent.Add(new StringContent(item.CollectionName, Encoding.UTF8, MediaTypeNames.Text.Plain), "CollectionName");
                    formContent.Add(new StringContent(pageNumber.ToString(), Encoding.UTF8, MediaTypeNames.Text.Plain), "PageNumber");
                    // if (lastSyncDate != null) formContent.Add(new StringContent(lastSyncDate.Value.ToString(), Encoding.UTF8, MediaTypeNames.Text.Plain), "LastSyncDate");
                     if (lastSyncedId != null) formContent.Add(new StringContent(lastSyncedId!.ToString(), Encoding.UTF8, MediaTypeNames.Text.Plain), "LastSyncedId");
                    httpRequestMessage.Content = formContent;

                    dataSyncResult = await _syncHttpService.MakeRequest<SyncResult>(httpRequestMessage);

                    if (dataSyncResult is null) break;

                    foreach(var rawDoc in dataSyncResult.Data)
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

        }

        public async Task StartSyncAsync()
        {

            if (_syncHasStarted || _syncIsStarting) return;

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
            await StartSyncAsync();
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
        private async Task<AppSyncMapping?> GetAppInformation()
        {
            var httpRequestMessage = new HttpRequestMessage();

            httpRequestMessage.RequestUri = new Uri($"{_apiUrl}/api/DataSync/Collect");
            httpRequestMessage.Method = HttpMethod.Post;
            var formContent = new MultipartFormDataContent();
            formContent.Add(new StringContent(_appName, Encoding.UTF8, MediaTypeNames.Text.Plain), "AppName");
            httpRequestMessage.Content = formContent;
            var httpResponse = await _syncHttpService.MakeRequest<AppSyncMapping>(httpRequestMessage);

            return httpResponse;

        }

        #endregion


    }
}
