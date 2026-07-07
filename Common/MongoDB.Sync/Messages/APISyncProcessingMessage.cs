using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Messages
{
    public class APISyncProcessingMessage : ValueChangedMessage<APISyncProcessingDetail>
    {
        public APISyncProcessingMessage(APISyncProcessingDetail value) : base(value)
        {
        }
    }
}
