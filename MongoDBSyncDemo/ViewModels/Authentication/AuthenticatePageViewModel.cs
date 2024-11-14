using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiteDB;
using MongoDB.Sync;
using MongoDB.Sync.Interfaces;
using MongoDB.Sync.Services;
using MongoDBSyncDemo.Services;
using System.Net.Mime;
using System.Text;

namespace MongoDBSyncDemo.ViewModels.Authentication
{
    public partial class AuthenticatePageViewModel : ViewModelBase
    {

        internal ISyncService _syncService;

        internal SyncHttpService _syncHttpService;

        internal AuthenticationService _authenticationService;

        private const string _loginUrl = "https://bootcomidentity.azurewebsites.net/AuthenticationV2";

        public AuthenticatePageViewModel(NavigationService navigationService, SyncHttpService syncHttpService, ISyncService syncService, AuthenticationService authenticationService) : base(navigationService)
        {
            _syncHttpService = syncHttpService;
            _syncService = syncService;
            _authenticationService = authenticationService;
        }

        public async override Task OnNavigatingTo(object? parameter)
        {
            if (String.IsNullOrWhiteSpace(InternalSettings.UserToken))
            {
                return;
            }

            _syncHttpService.RefreshToken = InternalSettings.RefreshToken;
            _syncHttpService.DeviceId = InternalSettings.DeviceId;
            _syncHttpService.JwtToken = InternalSettings.UserToken;

            await _syncService.StartSyncAsync();
        }

        [ObservableProperty]
        string? accessCode;

        [RelayCommand]
        async Task RequestAccessCode()
        {

            var formData = new MultipartFormDataContent();
            formData.Add(new StringContent("chris@bootcom.co.uk", Encoding.UTF8, MediaTypeNames.Text.Plain), "emailAddress");
            formData.Add(new StringContent(InternalSettings.DeviceId, Encoding.UTF8, MediaTypeNames.Text.Plain), "deviceId");

            var httpRequestMessage = new HttpRequestMessage();
            httpRequestMessage.Method = HttpMethod.Post;
            httpRequestMessage.RequestUri = new Uri($"{_loginUrl}/generate-access-code/BOOTCOM_HOME");
            httpRequestMessage.Content = formData;

            await _syncHttpService.MakeRequest(httpRequestMessage);
            
        }

        [RelayCommand]
        async Task CollectJwt()
        {
            var formData = new MultipartFormDataContent();
            formData.Add(new StringContent(AccessCode, Encoding.UTF8, MediaTypeNames.Text.Plain), "accessCode");
            formData.Add(new StringContent(InternalSettings.DeviceId, Encoding.UTF8, MediaTypeNames.Text.Plain), "deviceId");

            var httpRequestMessage = new HttpRequestMessage();
            httpRequestMessage.Method = HttpMethod.Post;
            httpRequestMessage.RequestUri = new Uri($"{_loginUrl}/verify-access-code");
            httpRequestMessage.Content = formData;
            var responseContent = await _syncHttpService.MakeRequest<Dictionary<string, string>>(httpRequestMessage);

            if (responseContent is null) return;
            
            InternalSettings.UserToken = responseContent["JwtToken"];
            InternalSettings.RefreshToken = responseContent["RefreshToken"];
            InternalSettings.UserId = new ObjectId(responseContent["UserId"]);

            _syncHttpService.RefreshToken = InternalSettings.RefreshToken;
            _syncHttpService.DeviceId = InternalSettings.DeviceId;
            _syncHttpService.JwtToken = InternalSettings.UserToken;

            await _syncService.StartSyncAsync();

        }


    }
}
