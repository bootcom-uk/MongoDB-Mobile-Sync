using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MongoDB.Sync.Messages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MongoDB.Sync.MAUI.Services
{
    public partial class InitialSyncProcessingService : ObservableObject 
    {

        [ObservableProperty]
        bool isRunning = false;

        [ObservableProperty]
        string processingMessage = string.Empty;

        [ObservableProperty]
        TimeSpan lastSyncTaken = TimeSpan.Zero;

        private readonly IMessenger _messenger;

        private Stopwatch _stopWatch;

        public InitialSyncProcessingService(IMessenger messenger) { 
            
            _messenger = messenger;

            _messenger.Register<APISyncStartedMessage>(this, (r, m) => {
                if (_stopWatch is null)
                {
                    _stopWatch = Stopwatch.StartNew();
                }
                else
                {
                    _stopWatch.Restart();
                }
                ProcessingMessage = "Sync Started";
                IsRunning = true;
            });

            _messenger.Register<APISyncProcessingMessage>(this, (r, m) => {
                ProcessingMessage = $"Processing page: {m.Value.PageNumber} for {m.Value.DatabaseName}_{m.Value.CollectionName}";
            });

            _messenger.Register<APISyncCompletedMessage>(this, (r, m) =>
            {
                _stopWatch?.Stop();
                LastSyncTaken = _stopWatch?.Elapsed ?? TimeSpan.Zero;
                ProcessingMessage = "Sync Complete";
                IsRunning = false;
            });
        }

        


    }
}
