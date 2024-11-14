using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Messages
{
    public class InitializeLocalDataMappingMessage : ValueChangedMessage<AppSyncMapping>
    {
        public InitializeLocalDataMappingMessage(AppSyncMapping value) : base(value)
        {
        }
    }
}
