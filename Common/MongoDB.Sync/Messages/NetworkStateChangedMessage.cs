using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Services;

namespace MongoDB.Sync.Messages
{
    public class NetworkStateChangedMessage : ValueChangedMessage<NetworkStateService.NetworkState>
    {
        public NetworkStateChangedMessage(NetworkStateService.NetworkState value) : base(value)
        {
        }
    }
}
