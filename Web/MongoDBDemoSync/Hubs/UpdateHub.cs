using Microsoft.AspNetCore.SignalR;

namespace MongoDBDemoSync.Hubs
{
    public class UpdateHub : Hub
    {
        public async Task SubscribeToApp(string appId)
        {
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
