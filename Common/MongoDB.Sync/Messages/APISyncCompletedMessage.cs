using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MongoDB.Sync.Messages
{
    public class APISyncCompletedMessage : ValueChangedMessage<bool>
    {
        public APISyncCompletedMessage(bool value) : base(value)
        {
        }
    }
}
