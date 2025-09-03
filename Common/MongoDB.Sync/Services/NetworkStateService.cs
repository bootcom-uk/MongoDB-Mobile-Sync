using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Messages;

namespace MongoDB.Sync.Services
{
    public partial class NetworkStateService(IMessenger messenger) : ObservableObject
    {

        public enum NetworkState
        {
            Connected,            
            Disconnected
        }

        private NetworkState _currentState = NetworkState.Connected;

        public NetworkState CurrentState { get => _currentState; internal set { _currentState = value; OnPropertyChanged(nameof(CurrentState)); } }

        internal void ChangeNetworkState(NetworkState currentState)
        {
            if (_currentState == currentState) return;

            _currentState = currentState;

            messenger.Send<NetworkStateChangedMessage>(new NetworkStateChangedMessage(_currentState));
        }

    }
}
