using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Messages;
using System;
using System.Collections.Generic;
using System.Text;

namespace MongoDB.Sync.MAUI.Services
{
    public partial class InitialSyncProcessingService : ObservableObject 
    {

        [ObservableProperty]
        bool isRunning = false;

        private readonly IMessenger _messenger;

        public InitialSyncProcessingService(IMessenger messenger) { 
            _messenger = messenger;

            _messenger.Register<APISyncStartedMessage>(this, (r, m) => {                
                IsRunning = true;
            });

            _messenger.Register<APISyncProcessingMessage>(this, (r, m) => {
                // Handle the message here
            });
        }

        


    }
}
