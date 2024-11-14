using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Messages;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Mime;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MongoDB.Sync.Services
{
    public class SyncHttpService
    {

        internal HttpClient _httpClient;

        public const string LoginURL = "https://bootcomidentity.azurewebsites.net/AuthenticationV2";

        public string? JwtToken { get; set; }

        public string? RefreshToken { get; set; }

        public string? DeviceId { get; set; }

        private IMessenger _messenger { get; }

        public SyncHttpService(HttpClient httpClient, IMessenger messenger) { 
            _httpClient = httpClient;
            _messenger = messenger;
        }

        public async Task MakeRequest(HttpRequestMessage httpRequestMessage)
        {
            var httpResponseMessage = await MakeRequestCore(httpRequestMessage);
            if (httpResponseMessage is null) return;
            httpResponseMessage.EnsureSuccessStatusCode();
            return;
        }

        public async Task<ResultType?> MakeRequest<ResultType>(HttpRequestMessage httpRequestMessage)
        {
            var httpResponseMessage = await MakeRequestCore(httpRequestMessage);
            if (httpResponseMessage is null) return default(ResultType);
            var content = await httpResponseMessage.Content.ReadAsStringAsync();

            var options = new JsonSerializerOptions() { 
                PropertyNameCaseInsensitive = true 
            };

            return await JsonSerializer.DeserializeAsync<ResultType>(await httpResponseMessage.Content.ReadAsStreamAsync(), options);
        }

        private async Task<HttpResponseMessage?> MakeRequestCore(HttpRequestMessage httpRequestMessage)
        {

            httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", JwtToken);

            var httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);

            // Throw an exception if the response message is null
            if (httpResponseMessage is null)
            {
                throw new Exception("The web request did not return a result");
            }

            // Successful response
            if (httpResponseMessage.IsSuccessStatusCode)
            {
                return httpResponseMessage;
            }

            // If this is an unauthorized request, then we need to use our refresh token 
            // to get a new authentication combination
            if (httpResponseMessage.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(RefreshToken))
            {
                var authenticationHttpRequestMessage = new HttpRequestMessage();
                authenticationHttpRequestMessage.Method = HttpMethod.Post;
                authenticationHttpRequestMessage.RequestUri = new Uri($"{LoginURL}/refresh-token");

                var formData = new MultipartFormDataContent();
                formData.Add(new StringContent(RefreshToken!, Encoding.UTF8, MediaTypeNames.Text.Plain), "refreshToken");
                formData.Add(new StringContent(DeviceId!, Encoding.UTF8, MediaTypeNames.Text.Plain), "deviceId");

                authenticationHttpRequestMessage.Content = formData;

                var authenticationHttpResponseMessage = await _httpClient.SendAsync(authenticationHttpRequestMessage);

                if (authenticationHttpResponseMessage is null)
                {
                    throw new Exception("The web request did not return a result");
                }

                if (!authenticationHttpResponseMessage.IsSuccessStatusCode)
                {
                    throw new AuthenticationException("We could not verify you're authentication state");
                }

                var authResponseContent = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(await authenticationHttpResponseMessage.Content.ReadAsStreamAsync());

                JwtToken = authResponseContent!["JwtToken"]!;
                RefreshToken = authResponseContent["RefreshToken"]!;

                _messenger.Send(new AuthenticationTokensChangedMessage(authResponseContent!));

                var clonedHttpRequestMessage = await CloneHttpRequestMessageAsync(httpRequestMessage);

                // Throw an exception if the response message is null
                if (clonedHttpRequestMessage is null)
                {
                    throw new Exception("The web request did not return a result");
                }

                var clonedHttpResponseMessage = await _httpClient.SendAsync(clonedHttpRequestMessage);

                clonedHttpResponseMessage.EnsureSuccessStatusCode();

                

                return clonedHttpResponseMessage;

            }

            return null;

        }

        

        private async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage req)
        {
            HttpRequestMessage clone = new(req.Method, req.RequestUri);

            // Copy the request's content (via a MemoryStream) into the cloned object
            var ms = new MemoryStream();
            if (req.Content != null)
            {
                await req.Content.CopyToAsync(ms).ConfigureAwait(false);
                ms.Position = 0;
                clone.Content = new StreamContent(ms);

                // Copy the content headers
                if (req.Content.Headers != null)
                    foreach (var h in req.Content.Headers)
                        clone.Content.Headers.Add(h.Key, h.Value);
            }

            if(req.Headers.Authorization != null)
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("bearer", JwtToken);
            }

            clone.Version = req.Version;

            foreach (KeyValuePair<string, IEnumerable<string>> header in req.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            return clone;
        }

    }
}
