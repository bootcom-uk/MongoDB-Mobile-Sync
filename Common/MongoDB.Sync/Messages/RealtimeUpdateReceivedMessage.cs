using CommunityToolkit.Mvvm.Messaging.Messages;
using MongoDB.Sync.Models;

namespace MongoDB.Sync.Messages
{
    public class RealtimeUpdateReceivedMessage : ValueChangedMessage<string>
    {
        public RealtimeUpdateReceivedMessage(string value) : base(value)
        {
        }
    }
}
