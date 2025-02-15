using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoDB.Sync.Services
{
    public partial class NetworkStateService : ObservableObject
    {

        public enum NetworkState
        {
            Connected,            
            Disconnected
        }

        private NetworkState _currentState;

        public NetworkState CurrentState { get => _currentState; internal set { _currentState = value; OnPropertyChanged(nameof(CurrentState)); } }

        internal void ChangeNetworkState(NetworkState currentState)
        {
            _currentState = currentState;
        }

    }
}
