using Microsoft.AspNetCore.SignalR;
using MongoDB.Sync.Web.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MongoDB.Sync.Web.Hubs
{
    public class UpdateHub : Hub
    {
        private readonly IGenericAuthService? _authService;

        public UpdateHub(IGenericAuthService? authService)
        {
            _authService = authService;
        }

        public override async Task OnConnectedAsync()
        {

            if (_authService == null)
            {
                // no auth service configured, allow all connections
                await base.OnConnectedAsync();
                return;
            }

            var http = Context.GetHttpContext();
            string? token = null;

            if (http?.Request?.Query != null && http.Request.Query.TryGetValue("access_token", out var qvals))
                token = qvals.FirstOrDefault();

            if (string.IsNullOrEmpty(token) && http?.Request?.Headers != null && http.Request.Headers.TryGetValue("Authorization", out var hvals))
            {
                var auth = hvals.FirstOrDefault();
                if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    token = auth.Substring("Bearer ".Length).Trim();
            }

            var userId = await _authService!.ValidateTokenAsync(http, token);

            if (!string.IsNullOrWhiteSpace(userId))
            {
                // refuse connection
                Context.Abort();
                return;
            }

            // keep the resolved user id on the Context for later checks
            Context.Items["userId"] = userId;

            await base.OnConnectedAsync();
        }

        public async Task SubscribeToApp(string appId)
        {

            if(_authService == null)
            {
                // no auth service configured, allow all subscriptions
                await Groups.AddToGroupAsync(Context.ConnectionId, appId);
                return;
            }

            // Ensure the connected principal is allowed to join this app group
            Context.Items.TryGetValue("userId", out var uidObj);
            var userId = uidObj as string;

            var allowed = await _authService.AuthorizeAppAsync(Context.GetHttpContext(), userId, appId);
            if (!allowed)
                throw new HubException("Not authorized to subscribe to this app.");

            await Groups.AddToGroupAsync(Context.ConnectionId, appId);
        }

        public async Task UnsubscribeFromApp(string appId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, appId);
        }

        // Sends updates to the specific app's group
        public async Task SendUpdate(string appId, object update)
        {
            await Clients.Group(appId).SendAsync("ReceiveUpdate", update);
        }
    }
}
