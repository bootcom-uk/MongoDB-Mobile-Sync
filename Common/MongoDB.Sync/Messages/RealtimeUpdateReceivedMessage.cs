using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Messages
{
    public class RealtimeUpdateReceivedMessage : ValueChangedMessage<UpdatedData>
    {
        public RealtimeUpdateReceivedMessage(UpdatedData value) : base(value)
        {
        }
    }
}
