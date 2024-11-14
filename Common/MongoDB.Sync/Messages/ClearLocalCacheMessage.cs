using CommunityToolkit.Mvvm.Messaging.Messages;

namespace MongoDB.Sync.Messages
{

    public class ClearLocalCacheMessage : ValueChangedMessage<bool>
    {
        /// <summary>
        /// Message to indicate whether we should clear the local cache
        /// </summary>
        /// <param name="value">Specifies whether we should rebuild the app mapping collection</param>
        public ClearLocalCacheMessage(bool value) : base(value)
        {
        }
    }
}
